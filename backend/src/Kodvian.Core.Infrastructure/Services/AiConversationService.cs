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
        return new AiConversationDetailDto
        {
            Id = conversation.Id,
            Title = conversation.Title,
            CreatedAtUtc = conversation.CreatedAtUtc,
            UpdatedAtUtc = conversation.UpdatedAtUtc,
            MessageCount = conversation.MessageCount,
            Messages = messages.Select(MapMessage).ToArray()
        };
    }

    public async Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, CancellationToken cancellationToken)
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
                return new AiSendMessageResultDto
                {
                    UserMessage = MapMessage(previousUser),
                    AssistantMessage = MapMessage(previousAssistant),
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
                var response = await responsesClient.CreateAsync(provider.Version, input, toolSchemas, cancellationToken);
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

                    try
                    {
                        var execution = await toolExecutor.ExecuteAsync(actor, run.Id, provider.Version, run.TraceId, toolName, arguments, cancellationToken);
                        sources.Add(execution.Source);
                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call_output",
                            ["call_id"] = callId,
                            ["output"] = execution.Data.ToJsonString()
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
            var grounded = sources.Count > 0;
            if (string.IsNullOrWhiteSpace(assistantText))
                assistantText = "No encontré información suficiente para responder con datos verificables.";
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
                CreatedAtUtc = DateTime.UtcNow
            };
            run.Status = partial ? "Partial" : "Completed";
            run.ToolCallCount = toolCalls;
            run.FinishedAtUtc = DateTime.UtcNow;
            conversation.UpdatedAtUtc = run.FinishedAtUtc.Value;
            dbContext.AiMessages.Add(assistantMessage);
            dbContext.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = actor.UserId, ConversationId = conversationId, RunId = run.Id, Decision = partial ? "Partial" : "Allowed", ReasonCode = partial ? "execution_limit" : "completed", CreatedAtUtc = DateTime.UtcNow });
            await dbContext.SaveChangesAsync(cancellationToken);

            return new AiSendMessageResultDto
            {
                UserMessage = MapMessage(userMessage),
                AssistantMessage = MapMessage(assistantMessage),
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

    private static AiMessageDto MapMessage(AiMessage message)
    {
        IReadOnlyCollection<AiSourceReferenceDto> sources;
        try { sources = JsonSerializer.Deserialize<AiSourceReferenceDto[]>(message.SourcesJson) ?? []; }
        catch (JsonException) { sources = []; }
        return new AiMessageDto { Id = message.Id, Role = message.Role, Content = message.Content, CreatedAtUtc = message.CreatedAtUtc, TraceId = message.TraceId, Sources = sources, Grounded = message.Grounded };
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
