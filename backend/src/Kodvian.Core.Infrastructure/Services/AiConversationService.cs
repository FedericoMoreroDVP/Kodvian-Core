using System.Text.Json;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class AiConversationService(
    KodvianDbContext dbContext,
    IAiProviderConfigurationService providerConfiguration,
    IAiResponsesClient responsesClient,
    IAiToolExecutor toolExecutor,
    Kodvian.Core.Application.Auth.Abstractions.ISessionValidator sessionValidator,
    ILogger<AiConversationService> logger) : IAiConversationService
{
    private const int MaxUserMessageLength = 4000;
    private const int MaxHistoryMessages = 20;
    private const int MaxHistoryCharacters = 12000;
    private const int MaxToolCalls = 12;
    private const int MaxModelCalls = 6;
    private const int MaxAssistantMessageLength = 16000;
    private const int MaxPendingActionsPerRun = 5;
    internal static readonly TimeSpan PendingActionLifetime = TimeSpan.FromMinutes(10);

    public async Task<AiConversationDto> CreateAsync(AiExecutionContext actor, AiConversationCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var title = request.Title?.Trim();
        if (title?.Length > 120) throw new ArgumentException("El título no puede superar los 120 caracteres.");
        var entity = new AiConversation
        {
            OwnerUserId = actor.UserId,
            Title = string.IsNullOrWhiteSpace(title) ? "Nueva conversación" : title,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        dbContext.AiConversations.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new AiConversationDto { Id = entity.Id, Title = entity.Title, CreatedAtUtc = entity.CreatedAtUtc, UpdatedAtUtc = entity.UpdatedAtUtc, MessageCount = 0 };
    }

    public async Task<PagedResultDto<AiConversationDto>> GetPagedAsync(AiExecutionContext actor, PagedRequestDto request, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        request.PageSize = Math.Min(request.PageSize, 50);
        var query = dbContext.AiConversations.AsNoTracking().Where(x => x.OwnerUserId == actor.UserId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.UpdatedAtUtc)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new AiConversationDto
            {
                Id = x.Id,
                Title = x.Title,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc,
                MessageCount = x.Messages.Count
            }).ToListAsync(cancellationToken);
        return new PagedResultDto<AiConversationDto> { Items = items, PageNumber = request.PageNumber, PageSize = request.PageSize, TotalCount = total };
    }

    public async Task<AiConversationDetailDto?> GetByIdAsync(AiExecutionContext actor, Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var conversation = await dbContext.AiConversations.AsNoTracking()
            .Where(x => x.Id == conversationId && x.OwnerUserId == actor.UserId)
            .Select(x => new { x.Id, x.Title, x.CreatedAtUtc, x.UpdatedAtUtc, MessageCount = x.Messages.Count })
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null) return null;
        var messages = await dbContext.AiMessages.AsNoTracking().Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var messageIds = messages.Select(x => x.Id).ToArray();
        var actions = await dbContext.AiPendingActions.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.MessageId.HasValue && messageIds.Contains(x.MessageId.Value))
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var actionsByMessage = actions.ToLookup(x => x.MessageId!.Value);
        return new AiConversationDetailDto
        {
            Id = conversation.Id,
            Title = conversation.Title,
            CreatedAtUtc = conversation.CreatedAtUtc,
            UpdatedAtUtc = conversation.UpdatedAtUtc,
            MessageCount = conversation.MessageCount,
            Messages = messages.Select(x => MapMessage(x, actionsByMessage[x.Id])).ToArray()
        };
    }

    public Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, CancellationToken cancellationToken) =>
        SendMessageAsync(actor, conversationId, request, null, cancellationToken);

    public async Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, IAiRunObserver? observer, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var content = request.Content?.Trim() ?? string.Empty;
        if (request.ClientRequestId == Guid.Empty) throw new ArgumentException("Falta el identificador de la solicitud.");
        if (content.Length is < 1 or > MaxUserMessageLength) throw new ArgumentException($"El mensaje debe tener entre 1 y {MaxUserMessageLength} caracteres.");

        var provider = await providerConfiguration.GetAsync(cancellationToken);
        if (!provider.IsConfigured || !provider.IsConnectionVerified) throw new AiProviderUnavailableException("Configura y verifica el proveedor antes de usar la Oficina Inteligente.");
        if (!provider.LiveDataApproved) throw new AiLiveDataApprovalRequiredException();

        var conversation = await dbContext.AiConversations.SingleOrDefaultAsync(
            x => x.Id == conversationId && x.OwnerUserId == actor.UserId, cancellationToken);
        if (conversation is null) throw new KeyNotFoundException("Conversación no encontrada.");

        var staleCutoff = DateTime.UtcNow.AddMinutes(-5);
        var staleRuns = await dbContext.AiRuns.Where(x => x.ConversationId == conversationId && x.Status == "Running" && x.StartedAtUtc < staleCutoff).ToListAsync(cancellationToken);
        foreach (var staleRun in staleRuns)
        {
            staleRun.Status = "Failed";
            staleRun.FinishedAtUtc = DateTime.UtcNow;
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = staleRun.Id, Decision = "Failed", ReasonCode = "stale_run_recovered", CreatedAtUtc = DateTime.UtcNow });
        }
        if (staleRuns.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);

        if (await dbContext.AiRuns.AnyAsync(x => x.ConversationId == conversationId && x.Status == "Running", cancellationToken))
            throw new AiConversationConflictException("Ya hay una consulta en curso en esta conversación.");

        var previousRun = await dbContext.AiRuns.SingleOrDefaultAsync(
            x => x.ConversationId == conversationId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (previousRun is not null)
        {
            if (previousRun.Status == "Completed" || previousRun.Status == "Partial")
            {
                var previousUser = await dbContext.AiMessages.AsNoTracking().SingleAsync(
                    x => x.ConversationId == conversationId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
                var previousAssistant = await dbContext.AiMessages.AsNoTracking().SingleAsync(x => x.RunId == previousRun.Id && x.Role == "assistant", cancellationToken);
                var previousActions = await dbContext.AiPendingActions.AsNoTracking().Where(x => x.MessageId == previousAssistant.Id).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
                return new AiSendMessageResultDto
                {
                    UserMessage = MapMessage(previousUser),
                    AssistantMessage = MapMessage(previousAssistant, previousActions),
                    RunId = previousRun.Id,
                    TraceId = previousRun.TraceId
                };
            }
            throw new AiConversationConflictException("Esta solicitud ya está siendo procesada o falló. Actualiza el historial antes de volver a intentarlo.");
        }

        var now = DateTime.UtcNow;
        var run = new AiRun
        {
            ConversationId = conversationId,
            ActorUserId = actor.UserId,
            ClientRequestId = request.ClientRequestId,
            Provider = provider.Provider,
            Model = provider.Model ?? string.Empty,
            ProviderConfigurationVersion = provider.Version,
            StartedAtUtc = now,
            TraceId = Guid.NewGuid().ToString("N")
        };
        var userMessage = new AiMessage
        {
            ConversationId = conversationId,
            RunId = run.Id,
            ClientRequestId = request.ClientRequestId,
            TraceId = run.TraceId,
            Role = "user",
            Content = content,
            CreatedAtUtc = now
        };
        if (conversation.Title == "Nueva conversación")
            conversation.Title = content.Length <= 120 ? content : content[..117] + "…";
        conversation.UpdatedAtUtc = now;
        conversation.Version = Guid.NewGuid();
        dbContext.AiRuns.Add(run);
        dbContext.AiMessages.Add(userMessage);
        dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = run.Id, Decision = "Started", ReasonCode = "user_message", CreatedAtUtc = now });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AiConversationConflictException("La conversación cambió. Actualiza e intenta nuevamente.");
        }
        catch (DbUpdateException)
        {
            var existing = await dbContext.AiRuns.AsNoTracking().SingleOrDefaultAsync(
                x => x.ConversationId == conversationId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            var running = await dbContext.AiRuns.AsNoTracking().AnyAsync(x => x.ConversationId == conversationId && x.Status == "Running", cancellationToken);
            if (existing is not null || running) throw new AiConversationConflictException("Ya hay una consulta en curso o esta solicitud ya fue recibida.");
            throw;
        }

        var sources = new List<AiSourceReferenceDto>();
        // Las propuestas se guardan recién al final, junto con el mensaje del asistente: si la ejecución falla no queda ninguna colgada.
        var pendingActions = new List<AiPendingAction>();
        var partial = false;
        try
        {
            var availableTools = toolExecutor.GetAvailableTools(actor);
            var toolSchemas = BuildToolSchemas(availableTools);
            var input = await BuildInputAsync(actor, conversationId, availableTools, cancellationToken);
            string assistantText = string.Empty;
            var toolCalls = 0;
            var completed = false;

            for (var modelCall = 0; modelCall < MaxModelCalls; modelCall++)
            {
                await EnsureActorAsync(actor, cancellationToken);
                var streamedText = false;
                AiModelResponseDto response;
                if (observer is null)
                {
                    response = await responsesClient.CreateAsync(provider.Version, input, toolSchemas, cancellationToken);
                }
                else
                {
                    await observer.OnStatusAsync("thinking", modelCall == 0 ? "Pensando…" : "Armando la respuesta…", null, cancellationToken);
                    response = await responsesClient.CreateStreamingAsync(provider.Version, input, toolSchemas, async (delta, ct) =>
                    {
                        streamedText = true;
                        await observer.OnTextDeltaAsync(delta, ct);
                    }, cancellationToken);
                }
                await EnsureActorAsync(actor, cancellationToken);
                await EnsureProviderApprovalAsync(provider.Version, cancellationToken);
                run.InputTokens += response.InputTokens;
                run.OutputTokens += response.OutputTokens;
                if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
                    throw new AiProviderUnavailableException("El proveedor no completó la respuesta.");

                var calls = response.OutputItems.OfType<JsonObject>()
                    .Where(x => string.Equals(x["type"]?.GetValue<string>(), "function_call", StringComparison.Ordinal))
                    .ToArray();
                foreach (var item in response.OutputItems) input.Add(item?.DeepClone());

                if (calls.Length == 0)
                {
                    assistantText = response.OutputText.Trim();
                    completed = true;
                    break;
                }
                // El texto previo a las herramientas ("Reviso las tareas…") es intermedio: la pantalla lo descarta.
                if (observer is not null && streamedText) await observer.OnTextResetAsync(cancellationToken);

                foreach (var call in calls)
                {
                    if (++toolCalls > MaxToolCalls)
                    {
                        partial = true;
                        assistantText = "Alcancé el límite de consultas para esta respuesta. Acotá el período o hacé una pregunta más específica.";
                        completed = true;
                        break;
                    }
                    var callId = call["call_id"]?.GetValue<string>();
                    var toolName = FromProviderToolName(call["name"]?.GetValue<string>() ?? string.Empty);
                    JsonObject arguments;
                    try { arguments = JsonNode.Parse(call["arguments"]?.GetValue<string>() ?? "{}") as JsonObject ?? new JsonObject(); }
                    catch (JsonException) { arguments = new JsonObject(); }
                    if (observer is not null) await observer.OnStatusAsync("tool", AiToolLabels.Progress(toolName), toolName, cancellationToken);

                    try
                    {
                        var execution = await toolExecutor.ExecuteAsync(actor, run.Id, provider.Version, run.TraceId, toolName, arguments, cancellationToken);
                        if (execution.Source is not null) sources.Add(execution.Source);
                        var output = execution.Data;
                        if (execution.Proposal is not null)
                        {
                            if (pendingActions.Count >= MaxPendingActionsPerRun)
                                throw new AiToolRejectedException($"Solo se pueden proponer {MaxPendingActionsPerRun} acciones por respuesta. Pedile al usuario que confirme las anteriores.");
                            var pending = CreatePendingAction(actor, conversationId, run.Id, execution.Proposal);
                            pendingActions.Add(pending);
                            output = (JsonObject)output.DeepClone();
                            output["actionId"] = pending.Id.ToString();
                        }
                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call_output",
                            ["call_id"] = callId,
                            ["output"] = output.ToJsonString()
                        });
                    }
                    catch (Exception exception) when (exception is AiToolRejectedException or ArgumentException or KeyNotFoundException)
                    {
                        // Los errores de validación o de dominio vuelven al modelo para que pueda explicarlos o pedir el dato que falta.
                        // La sesión o la aprobación revocadas siguen cortando la ejecución.
                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call_output",
                            ["call_id"] = callId,
                            ["output"] = JsonSerializer.Serialize(new { error = exception.Message })
                        });
                    }
                }
                if (completed) break;
            }

            if (!completed)
            {
                partial = true;
                assistantText = "No pude completar la consulta dentro de los límites de esta ejecución. Acotá la pregunta y volvé a intentar.";
            }
            // Las respuestas sin consultas (saludos, ayuda, "no puedo hacer X") se guardan tal cual, marcadas como no respaldadas.
            var grounded = sources.Count > 0 || pendingActions.Count > 0;
            if (string.IsNullOrWhiteSpace(assistantText))
                assistantText = pendingActions.Count > 0
                    ? "Te dejé la acción lista. Revisala y confirmala con el botón de la tarjeta."
                    : "No encontré información suficiente para responder con datos verificables.";
            var (cleanText, suggestions) = AiSuggestionParser.Extract(assistantText);
            assistantText = string.IsNullOrWhiteSpace(cleanText) ? assistantText : cleanText;
            if (assistantText.Length > MaxAssistantMessageLength) assistantText = assistantText[..MaxAssistantMessageLength];
            await EnsureActorAsync(actor, cancellationToken);
            await EnsureProviderApprovalAsync(provider.Version, cancellationToken);

            var uniqueSources = sources.GroupBy(x => (x.ToolName, x.From, x.To, x.Currency)).Select(x => x.First()).ToArray();
            var assistantMessage = new AiMessage
            {
                ConversationId = conversationId,
                RunId = run.Id,
                TraceId = run.TraceId,
                Role = "assistant",
                Content = assistantText,
                SourcesJson = JsonSerializer.Serialize(uniqueSources),
                Grounded = grounded,
                SuggestionsJson = JsonSerializer.Serialize(suggestions),
                CreatedAtUtc = DateTime.UtcNow
            };
            run.Status = partial ? "Partial" : "Completed";
            run.ToolCallCount = toolCalls;
            run.FinishedAtUtc = DateTime.UtcNow;
            conversation.UpdatedAtUtc = run.FinishedAtUtc.Value;
            dbContext.AiMessages.Add(assistantMessage);
            foreach (var pending in pendingActions)
            {
                pending.MessageId = assistantMessage.Id;
                dbContext.AiPendingActions.Add(pending);
            }
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = run.Id, Decision = partial ? "Partial" : "Allowed", ReasonCode = partial ? "execution_limit" : "completed", CreatedAtUtc = DateTime.UtcNow });
            await dbContext.SaveChangesAsync(cancellationToken);

            return new AiSendMessageResultDto
            {
                UserMessage = MapMessage(userMessage),
                AssistantMessage = MapMessage(assistantMessage, pendingActions),
                RunId = run.Id,
                TraceId = run.TraceId
            };
        }
        catch (Exception exception)
        {
            run.Status = exception is OperationCanceledException ? "Cancelled" : "Failed";
            run.FinishedAtUtc = DateTime.UtcNow;
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = run.Id, Decision = run.Status, ReasonCode = SafeReasonCode(exception), CreatedAtUtc = DateTime.UtcNow });
            await dbContext.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("AI run {RunId} for actor {ActorId} failed with {ErrorType}.", run.Id, actor.UserId, exception.GetType().Name);
            throw;
        }
    }

    public async Task<AiActionResolutionDto> ConfirmActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var (conversation, action) = await LoadOwnedActionAsync(actor, conversationId, actionId, cancellationToken);
        await EnsurePendingAsync(actor, action, cancellationToken);

        var outcome = await ExecutePendingAsync(actor, action, cancellationToken);
        var resultMessage = outcome.Success
            ? NewSystemReply(conversationId, outcome.Text, outcome.Source is null ? [] : [outcome.Source], grounded: true)
            : NewSystemReply(conversationId, $"No pude completar «{action.Title}»: {outcome.Text}", [], grounded: false);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        dbContext.AiMessages.Add(resultMessage);
        // La escritura ya ocurrió: el resultado se guarda aunque el usuario cierre la pantalla.
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new AiActionResolutionDto { Action = MapAction(action), Message = MapMessage(resultMessage) };
    }

    public async Task<AiActionBatchResolutionDto> ConfirmAllActionsAsync(AiExecutionContext actor, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var conversation = await dbContext.AiConversations.SingleOrDefaultAsync(x => x.Id == conversationId && x.OwnerUserId == actor.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversación no encontrada.");
        var actions = await dbContext.AiPendingActions
            .Where(x => x.ConversationId == conversationId && x.MessageId == messageId && x.ActorUserId == actor.UserId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var pending = actions.Where(x => x.Status == "Pending" && x.ExpiresAtUtc > now).ToList();
        if (pending.Count == 0) throw new AiConversationConflictException("No quedan acciones pendientes para confirmar en este mensaje.");

        // Cada acción se ejecuta por separado: si una falla, las demás siguen y el resumen lo explica.
        var lines = new List<string>();
        var sources = new List<AiSourceReferenceDto>();
        foreach (var action in pending)
        {
            var outcome = await ExecutePendingAsync(actor, action, cancellationToken);
            lines.Add(outcome.Success ? $"- ✅ {outcome.Text}" : $"- ⚠️ No pude completar «{action.Title}»: {outcome.Text}");
            if (outcome.Source is not null && !sources.Any(x => x.ToolName == outcome.Source.ToolName)) sources.Add(outcome.Source);
        }
        foreach (var expired in actions.Where(x => x.Status == "Pending" && x.ExpiresAtUtc <= now))
        {
            expired.Status = "Expired";
            expired.ResolvedAtUtc = now;
            expired.Version = Guid.NewGuid();
        }
        var succeeded = pending.Count(x => x.Status == "Confirmed");
        var header = succeeded == pending.Count ? $"Listo, confirmé las {pending.Count} acciones:" : $"Confirmé {succeeded} de {pending.Count} acciones:";
        var message = NewSystemReply(conversationId, header + "\n" + string.Join("\n", lines), sources.ToArray(), grounded: succeeded > 0);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        dbContext.AiMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new AiActionBatchResolutionDto { Actions = actions.Select(MapAction).ToArray(), Message = MapMessage(message) };
    }

    public async Task<AiActionResolutionDto> UndoActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var (conversation, action) = await LoadOwnedActionAsync(actor, conversationId, actionId, cancellationToken);
        if (!CanUndo(action, DateTime.UtcNow))
            throw new AiConversationConflictException(action.UndoneAtUtc.HasValue ? "La acción ya se deshizo." : "Esta acción ya no se puede deshacer.");

        JsonObject undoData;
        try { undoData = JsonNode.Parse(action.UndoJson!) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { undoData = new JsonObject(); }

        // Mismo control de concurrencia que al confirmar: un solo "deshacer" por acción.
        var undoneAt = DateTime.UtcNow;
        action.UndoneAtUtc = undoneAt;
        action.Version = Guid.NewGuid();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AiConversationConflictException("La acción cambió mientras la deshacías. Actualizá la conversación.");
        }

        AiMessage message;
        try
        {
            var outcome = await toolExecutor.UndoActionAsync(actor, action.Id, action.ToolName, action.ResultEntityId!.Value, undoData, cancellationToken);
            action.Status = "Undone";
            message = NewSystemReply(conversationId, outcome.Message, outcome.Source is null ? [] : [outcome.Source], grounded: true);
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = action.RunId, ToolName = action.ToolName, Decision = "Undone", ReasonCode = "user_undo", CreatedAtUtc = DateTime.UtcNow });
        }
        catch (AiToolRejectedException exception)
        {
            // No se pudo deshacer (por ejemplo, alguien ya la modificó): queda confirmada y se explica por qué.
            action.UndoneAtUtc = null;
            message = NewSystemReply(conversationId, $"No pude deshacer «{action.Title}»: {exception.Message}", [], grounded: false);
        }
        catch
        {
            action.UndoneAtUtc = null;
            action.Version = Guid.NewGuid();
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        action.Version = Guid.NewGuid();
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        dbContext.AiMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new AiActionResolutionDto { Action = MapAction(action), Message = MapMessage(message) };
    }

    private sealed record PendingOutcome(bool Success, string Text, AiSourceReferenceDto? Source);

    private async Task<PendingOutcome> ExecutePendingAsync(AiExecutionContext actor, AiPendingAction action, CancellationToken cancellationToken)
    {
        // Se marca en ejecución antes de escribir: el token de concurrencia evita que dos confirmaciones creen la tarea dos veces.
        action.Status = "Executing";
        action.Version = Guid.NewGuid();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AiConversationConflictException("La acción ya se está procesando.");
        }

        JsonObject arguments;
        try { arguments = JsonNode.Parse(action.ArgumentsJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { arguments = new JsonObject(); }

        PendingOutcome result;
        try
        {
            var outcome = await toolExecutor.ExecuteConfirmedActionAsync(actor, action.Id, action.ToolName, arguments, cancellationToken);
            action.Status = "Confirmed";
            action.ResultMessage = Truncate(outcome.Message, 1000);
            action.ResultLink = outcome.Link;
            action.ResultEntityId = outcome.EntityId;
            action.UndoJson = outcome.UndoData?.ToJsonString();
            result = new PendingOutcome(true, outcome.Message, outcome.Source);
        }
        catch (AiToolRejectedException exception)
        {
            action.Status = "Failed";
            action.ResultMessage = Truncate(exception.Message, 1000);
            result = new PendingOutcome(false, exception.Message, null);
        }
        catch (Exception exception)
        {
            // Sesión vencida u otro error inesperado: la acción queda pendiente para reintentar y no se informa el detalle.
            action.Status = "Pending";
            action.Version = Guid.NewGuid();
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = action.ConversationId, RunId = action.RunId, ToolName = action.ToolName, Decision = "Failed", ReasonCode = SafeReasonCode(exception), CreatedAtUtc = DateTime.UtcNow });
            await dbContext.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("AI action {ActionId} for actor {ActorId} failed with {ErrorType}.", action.Id, actor.UserId, exception.GetType().Name);
            throw;
        }

        var now = DateTime.UtcNow;
        action.ResolvedAtUtc = now;
        action.Version = Guid.NewGuid();
        dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = action.ConversationId, RunId = action.RunId, ToolName = action.ToolName, Decision = action.Status, ReasonCode = action.Status == "Confirmed" ? "user_confirmed" : "action_failed", CreatedAtUtc = now });
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return result;
    }

    internal static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(15);

    private static bool CanUndo(AiPendingAction action, DateTime now) =>
        action.Status == "Confirmed" && action.UndoJson is not null && action.ResultEntityId.HasValue && !action.UndoneAtUtc.HasValue
        && action.ResolvedAtUtc.HasValue && action.ResolvedAtUtc.Value.Add(UndoWindow) > now;

    public async Task<AiActionResolutionDto> CancelActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var (_, action) = await LoadOwnedActionAsync(actor, conversationId, actionId, cancellationToken);
        await EnsurePendingAsync(actor, action, cancellationToken);
        var now = DateTime.UtcNow;
        action.Status = "Cancelled";
        action.ResolvedAtUtc = now;
        action.Version = Guid.NewGuid();
        dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = action.RunId, ToolName = action.ToolName, Decision = "Cancelled", ReasonCode = "user_cancelled", CreatedAtUtc = now });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AiConversationConflictException("La acción cambió mientras la cancelabas. Actualizá la conversación.");
        }
        return new AiActionResolutionDto { Action = MapAction(action) };
    }

    private async Task<(AiConversation Conversation, AiPendingAction Action)> LoadOwnedActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken)
    {
        var conversation = await dbContext.AiConversations.SingleOrDefaultAsync(x => x.Id == conversationId && x.OwnerUserId == actor.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversación no encontrada.");
        var action = await dbContext.AiPendingActions.SingleOrDefaultAsync(x => x.Id == actionId && x.ConversationId == conversationId && x.ActorUserId == actor.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Acción no encontrada.");
        return (conversation, action);
    }

    private async Task EnsurePendingAsync(AiExecutionContext actor, AiPendingAction action, CancellationToken cancellationToken)
    {
        if (action.Status == "Pending" && action.ExpiresAtUtc <= DateTime.UtcNow)
        {
            action.Status = "Expired";
            action.ResolvedAtUtc = DateTime.UtcNow;
            action.Version = Guid.NewGuid();
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = action.ConversationId, RunId = action.RunId, ToolName = action.ToolName, Decision = "Expired", ReasonCode = "action_expired", CreatedAtUtc = DateTime.UtcNow });
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new AiConversationConflictException("La acción venció. Pedile al asistente que la prepare de nuevo.");
        }
        if (action.Status != "Pending")
            throw new AiConversationConflictException(action.Status switch
            {
                "Confirmed" => "La acción ya fue confirmada.",
                "Cancelled" => "La acción ya fue cancelada.",
                "Expired" => "La acción venció. Pedile al asistente que la prepare de nuevo.",
                "Executing" => "La acción ya se está procesando.",
                _ => "La acción ya no se puede confirmar."
            });
    }

    private static AiPendingAction CreatePendingAction(AiExecutionContext actor, Guid conversationId, Guid runId, AiActionProposalDto proposal)
    {
        var now = DateTime.UtcNow;
        return new AiPendingAction
        {
            ConversationId = conversationId,
            RunId = runId,
            ActorUserId = actor.UserId,
            ToolName = proposal.ToolName,
            ArgumentsJson = proposal.Arguments.ToJsonString(),
            Title = Truncate(proposal.Title, 200),
            Summary = Truncate(proposal.Summary, 1000),
            DetailsJson = JsonSerializer.Serialize(proposal.Details),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(PendingActionLifetime)
        };
    }

    private static AiMessage NewSystemReply(Guid conversationId, string content, AiSourceReferenceDto[] sources, bool grounded) => new()
    {
        ConversationId = conversationId,
        Role = "assistant",
        Content = Truncate(content, MaxAssistantMessageLength),
        SourcesJson = JsonSerializer.Serialize(sources),
        Grounded = grounded,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private static AiPendingActionDto MapAction(AiPendingAction action)
    {
        IReadOnlyCollection<AiActionDetailDto> details;
        try { details = JsonSerializer.Deserialize<AiActionDetailDto[]>(action.DetailsJson) ?? []; }
        catch (JsonException) { details = []; }
        var status = action.Status == "Pending" && action.ExpiresAtUtc <= DateTime.UtcNow ? "Expired" : action.Status;
        return new AiPendingActionDto
        {
            Id = action.Id,
            ToolName = action.ToolName,
            Title = action.Title,
            Summary = action.Summary,
            Details = details,
            Status = status,
            ExpiresAtUtc = action.ExpiresAtUtc,
            ResolvedAtUtc = action.ResolvedAtUtc,
            ResultMessage = action.ResultMessage,
            ResultLink = action.ResultLink,
            CanUndo = CanUndo(action, DateTime.UtcNow),
            UndoAvailableUntilUtc = CanUndo(action, DateTime.UtcNow) ? action.ResolvedAtUtc!.Value.Add(UndoWindow) : null
        };
    }

    public async Task<bool> DeleteAsync(AiExecutionContext actor, Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var conversation = await dbContext.AiConversations.SingleOrDefaultAsync(x => x.Id == conversationId && x.OwnerUserId == actor.UserId, cancellationToken);
        if (conversation is null) return false;
        if (await dbContext.AiRuns.AnyAsync(x => x.ConversationId == conversationId && x.Status == "Running", cancellationToken))
            throw new AiConversationConflictException("No se puede eliminar la conversación mientras hay una consulta en curso.");
        dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, Decision = "Deleted", ReasonCode = "owner_delete", CreatedAtUtc = DateTime.UtcNow });
        dbContext.AiConversations.Remove(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task EnsureActorAsync(AiExecutionContext actor, CancellationToken cancellationToken)
    {
        if (!actor.Roles.Contains(RoleNames.Administrator) || !actor.Permissions.Contains(PermissionCodes.AiOfficeUse))
            throw new UnauthorizedAccessException("No tienes acceso a Oficina Inteligente.");
        if (!await sessionValidator.IsValidAsync(actor.UserId, actor.SessionVersion, cancellationToken))
            throw new UnauthorizedAccessException("La sesión dejó de estar vigente.");
    }

    private async Task EnsureProviderApprovalAsync(Guid expectedVersion, CancellationToken cancellationToken)
    {
        var current = await providerConfiguration.GetAsync(cancellationToken);
        if (current.Version != expectedVersion || !current.IsConnectionVerified || !current.LiveDataApproved)
            throw new AiLiveDataApprovalRequiredException();
    }

    private async Task<JsonArray> BuildInputAsync(AiExecutionContext actor, Guid conversationId, IReadOnlyCollection<AiToolDefinitionDto> tools, CancellationToken cancellationToken)
    {
        var userName = await dbContext.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.FullName).SingleOrDefaultAsync(cancellationToken);
        var systemPrompt = AiSystemPromptBuilder.Build(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone, userName, tools);
        var recent = await dbContext.AiMessages.AsNoTracking().Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(MaxHistoryMessages)
            .Select(x => new { x.Role, x.Content, x.CreatedAtUtc }).ToListAsync(cancellationToken);
        var history = new List<(string Role, string Content)>();
        var historyCharacters = 0;
        foreach (var message in recent)
        {
            if (history.Count > 0 && historyCharacters + message.Content.Length > MaxHistoryCharacters) break;
            history.Add((message.Role, message.Content));
            historyCharacters += message.Content.Length;
        }
        history.Reverse();
        var input = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = systemPrompt })
            }
        };
        foreach (var message in history)
            input.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
        return input;
    }

    private static JsonArray BuildToolSchemas(IReadOnlyCollection<AiToolDefinitionDto> definitions)
    {
        var tools = new JsonArray();
        foreach (var definition in definitions)
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = ToProviderToolName(definition.Name),
                ["description"] = definition.Description,
                ["parameters"] = definition.Parameters.DeepClone()
            });
        return tools;
    }

    // OpenAI function names only accept [a-zA-Z0-9_-]; internal names use dots (e.g. "projects.list").
    internal static string ToProviderToolName(string name) => name.Replace(".", "__", StringComparison.Ordinal);

    internal static string FromProviderToolName(string name) => name.Replace("__", ".", StringComparison.Ordinal);

    private static AiMessageDto MapMessage(AiMessage message) => MapMessage(message, []);

    private static AiMessageDto MapMessage(AiMessage message, IEnumerable<AiPendingAction> actions)
    {
        IReadOnlyCollection<AiSourceReferenceDto> sources;
        try { sources = JsonSerializer.Deserialize<AiSourceReferenceDto[]>(message.SourcesJson) ?? []; }
        catch (JsonException) { sources = []; }
        IReadOnlyCollection<string> suggestions;
        try { suggestions = JsonSerializer.Deserialize<string[]>(message.SuggestionsJson) ?? []; }
        catch (JsonException) { suggestions = []; }
        return new AiMessageDto
        {
            Id = message.Id, Role = message.Role, Content = message.Content, CreatedAtUtc = message.CreatedAtUtc, TraceId = message.TraceId,
            Sources = sources, Grounded = message.Grounded, PendingActions = actions.Select(MapAction).ToArray(), Suggestions = suggestions
        };
    }

    private static string SafeReasonCode(Exception exception) => exception switch
    {
        AiProviderUnavailableException => "provider_unavailable",
        AiLiveDataApprovalRequiredException => "approval_required",
        AiToolRejectedException => "tool_rejected",
        UnauthorizedAccessException => "authorization_denied",
        _ => "execution_error"
    };
}
