using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

/// <summary>Flujo de confirmación en dos pasos de las acciones del asistente.</summary>
public sealed class AiPendingActionTests
{
    [Fact]
    public async Task ProposalIsStoredAsPendingActionAndNothingIsExecuted()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var model = new ProposeOnceModel();
        var (service, actor, conversationId) = await CreateAsync(db, model, executor);

        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Agendame llamar a Juan mañana" }, CancellationToken.None);

        var card = Assert.Single(result.AssistantMessage.PendingActions);
        Assert.Equal("Pending", card.Status);
        Assert.Equal("Crear tarea", card.Title);
        Assert.Contains(card.Details, x => x.Label == "Proyecto" && x.Value == "Proyecto Demo");
        Assert.True(result.AssistantMessage.Grounded);
        Assert.Equal(0, executor.ConfirmedCalls);
        Assert.Contains("pending_confirmation", model.SecondInput);
        Assert.Contains(card.Id.ToString(), model.SecondInput);

        var stored = await db.AiPendingActions.SingleAsync();
        Assert.Equal(result.AssistantMessage.Id, stored.MessageId);
        Assert.True(stored.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(9));

        var detail = await service.GetByIdAsync(actor, conversationId, CancellationToken.None);
        Assert.Single(detail!.Messages.Single(x => x.Role == "assistant").PendingActions);
    }

    [Fact]
    public async Task ConfirmExecutesOnceAndAddsResultMessage()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);
        var actionId = result.AssistantMessage.PendingActions.Single().Id;

        var confirmed = await service.ConfirmActionAsync(actor, conversationId, actionId, CancellationToken.None);

        Assert.Equal("Confirmed", confirmed.Action.Status);
        Assert.Equal("/tareas", confirmed.Action.ResultLink);
        Assert.Equal("Listo, creé la tarea «Llamar a Juan».", confirmed.Message!.Content);
        Assert.Equal(1, executor.ConfirmedCalls);
        Assert.Equal("tasks.create", executor.LastToolName);
        Assert.Equal("Llamar a Juan", executor.LastArguments!["title"]!.GetValue<string>());
        Assert.Contains(await db.AiAuditEvents.ToListAsync(), x => x.Decision == "Confirmed" && x.ReasonCode == "user_confirmed");

        var again = await Assert.ThrowsAsync<AiConversationConflictException>(() => service.ConfirmActionAsync(actor, conversationId, actionId, CancellationToken.None));
        Assert.Contains("ya fue confirmada", again.Message);
        Assert.Equal(1, executor.ConfirmedCalls);

        var detail = await service.GetByIdAsync(actor, conversationId, CancellationToken.None);
        Assert.Contains(detail!.Messages, x => x.Content == "Listo, creé la tarea «Llamar a Juan».");
    }

    [Fact]
    public async Task CancelledActionCannotBeConfirmed()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);
        var actionId = result.AssistantMessage.PendingActions.Single().Id;

        var cancelled = await service.CancelActionAsync(actor, conversationId, actionId, CancellationToken.None);

        Assert.Equal("Cancelled", cancelled.Action.Status);
        await Assert.ThrowsAsync<AiConversationConflictException>(() => service.ConfirmActionAsync(actor, conversationId, actionId, CancellationToken.None));
        Assert.Equal(0, executor.ConfirmedCalls);
    }

    [Fact]
    public async Task ExpiredActionIsRejectedAndMarkedExpired()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);
        var stored = await db.AiPendingActions.SingleAsync();
        stored.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var detail = await service.GetByIdAsync(actor, conversationId, CancellationToken.None);
        Assert.Equal("Expired", detail!.Messages.Single(x => x.Role == "assistant").PendingActions.Single().Status);

        var error = await Assert.ThrowsAsync<AiConversationConflictException>(() => service.ConfirmActionAsync(actor, conversationId, stored.Id, CancellationToken.None));
        Assert.Contains("venció", error.Message);
        Assert.Equal("Expired", (await db.AiPendingActions.SingleAsync()).Status);
        Assert.Equal(0, executor.ConfirmedCalls);
    }

    [Fact]
    public async Task DomainRejectionOnConfirmMarksTheActionFailedAndExplainsIt()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor { ConfirmFailure = new AiToolRejectedException("El proyecto indicado no existe o no está activo.") };
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);

        var resolution = await service.ConfirmActionAsync(actor, conversationId, result.AssistantMessage.PendingActions.Single().Id, CancellationToken.None);

        Assert.Equal("Failed", resolution.Action.Status);
        Assert.Contains("no existe", resolution.Message!.Content);
        Assert.False(resolution.Message.Grounded);
    }

    [Fact]
    public async Task OtherUsersCannotConfirmTheAction()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ConfirmActionAsync(Actor(), conversationId, result.AssistantMessage.PendingActions.Single().Id, CancellationToken.None));
        Assert.Equal(0, executor.ConfirmedCalls);
    }

    private static async Task<(AiConversationService Service, AiExecutionContext Actor, Guid ConversationId)> CreateAsync(KodvianDbContext db, IAiResponsesClient model, IAiToolExecutor executor)
    {
        var service = new AiConversationService(db, new ApprovedProvider(), model, executor, new ActiveSessionValidator(), NullLogger<AiConversationService>.Instance);
        var actor = Actor();
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);
        return (service, actor, conversation.Id);
    }

    private static KodvianDbContext CreateDatabase()
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiExecutionContext Actor() => new()
    {
        UserId = Guid.NewGuid(),
        SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([RoleNames.Administrator], StringComparer.Ordinal),
        Permissions = new HashSet<string>([PermissionCodes.AiOfficeUse, PermissionCodes.TasksRead, PermissionCodes.TasksWrite], StringComparer.Ordinal)
    };

    private sealed class ActiveSessionValidator : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ApprovedProvider : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto
        {
            IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true, Provider = "Anthropic", Model = "model-test"
        });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    /// <summary>Primera llamada: pide tasks.create. Segunda: responde con texto.</summary>
    private sealed class ProposeOnceModel(params string[] titles) : IAiResponsesClient
    {
        private int _calls;
        public string SecondInput { get; private set; } = string.Empty;

        public Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken)
        {
            if (++_calls == 1)
                return Task.FromResult(new AiModelResponseDto
                {
                    Status = "completed",
                    OutputItems = new JsonArray((titles.Length == 0 ? ["Llamar a Juan"] : titles).Select((title, index) => (JsonNode?)new JsonObject
                    {
                        ["type"] = "function_call", ["name"] = "tasks__create", ["call_id"] = $"call-{index}",
                        ["arguments"] = $"{{\"projectId\":\"7d7c2a4e-0000-4000-8000-000000000001\",\"title\":\"{title}\"}}"
                    }).ToArray())
                });
            SecondInput = input.ToJsonString();
            return Task.FromResult(new AiModelResponseDto { Status = "completed", OutputText = "Te dejé la tarea lista para confirmar." });
        }
    }

    private sealed class ProposalExecutor : IAiToolExecutor
    {
        public int ConfirmedCalls { get; private set; }
        public string? LastToolName { get; private set; }
        public JsonObject? LastArguments { get; private set; }
        public Exception? ConfirmFailure { get; init; }

        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) =>
            [new AiToolDefinitionDto { Name = "tasks.create", RequiresConfirmation = true, Description = "Crear", Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() } }];

        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid expectedConfigurationVersion, string traceId, string toolName, JsonObject arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new AiToolExecutionResultDto
            {
                Data = new JsonObject { ["status"] = "pending_confirmation" },
                Proposal = new AiActionProposalDto
                {
                    ToolName = toolName,
                    Title = "Crear tarea",
                    Summary = "Crear la tarea «Llamar a Juan» en Proyecto Demo.",
                    Details = [new AiActionDetailDto { Label = "Proyecto", Value = "Proyecto Demo" }],
                    Arguments = (JsonObject)arguments.DeepClone()
                }
            });

        public Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken cancellationToken)
        {
            ConfirmedCalls++;
            LastToolName = toolName;
            LastArguments = arguments;
            if (ConfirmFailure is not null) return Task.FromException<AiActionOutcomeDto>(ConfirmFailure);
            return Task.FromResult(new AiActionOutcomeDto
            {
                Message = $"Listo, creé la tarea «{arguments["title"]!.GetValue<string>()}».",
                Link = "/tareas",
                Source = new AiSourceReferenceDto { ToolName = toolName, Source = "test", AsOfUtc = DateTime.UtcNow, Link = "/tareas" },
                EntityId = Guid.NewGuid(),
                UndoData = new JsonObject()
            });
        }

        public int UndoCalls { get; private set; }

        public Task<AiActionOutcomeDto> UndoActionAsync(AiExecutionContext actor, Guid actionId, string toolName, Guid entityId, JsonObject undoData, CancellationToken cancellationToken)
        {
            UndoCalls++;
            return Task.FromResult(new AiActionOutcomeDto { Message = "Deshice la creación." });
        }
    }

    [Fact]
    public async Task ConfirmAllExecutesEveryPendingActionAndAddsOneSummary()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel("Tarea A", "Tarea B", "Tarea C"), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creame tres tareas" }, CancellationToken.None);
        Assert.Equal(3, result.AssistantMessage.PendingActions.Count);

        var batch = await service.ConfirmAllActionsAsync(actor, conversationId, result.AssistantMessage.Id, CancellationToken.None);

        Assert.All(batch.Actions, x => Assert.Equal("Confirmed", x.Status));
        Assert.Equal(3, executor.ConfirmedCalls);
        Assert.StartsWith("Listo, confirmé las 3 acciones:", batch.Message!.Content);
        Assert.Contains("Tarea B", batch.Message.Content);
        Assert.Single(await db.AiMessages.Where(x => x.Content.StartsWith("Listo, confirmé")).ToListAsync());
        await Assert.ThrowsAsync<AiConversationConflictException>(() => service.ConfirmAllActionsAsync(actor, conversationId, result.AssistantMessage.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmedActionCanBeUndoneOnlyOnce()
    {
        await using var db = CreateDatabase();
        var executor = new ProposalExecutor();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), executor);
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);
        var actionId = result.AssistantMessage.PendingActions.Single().Id;
        var confirmed = await service.ConfirmActionAsync(actor, conversationId, actionId, CancellationToken.None);
        Assert.True(confirmed.Action.CanUndo);

        var undone = await service.UndoActionAsync(actor, conversationId, actionId, CancellationToken.None);

        Assert.Equal("Undone", undone.Action.Status);
        Assert.False(undone.Action.CanUndo);
        Assert.Equal("Deshice la creación.", undone.Message!.Content);
        await Assert.ThrowsAsync<AiConversationConflictException>(() => service.UndoActionAsync(actor, conversationId, actionId, CancellationToken.None));
        Assert.Equal(1, executor.UndoCalls);
    }

    [Fact]
    public async Task UndoIsNotOfferedAfterTheWindowExpires()
    {
        await using var db = CreateDatabase();
        var (service, actor, conversationId) = await CreateAsync(db, new ProposeOnceModel(), new ProposalExecutor());
        var result = await service.SendMessageAsync(actor, conversationId, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá la tarea" }, CancellationToken.None);
        var actionId = result.AssistantMessage.PendingActions.Single().Id;
        await service.ConfirmActionAsync(actor, conversationId, actionId, CancellationToken.None);
        var stored = await db.AiPendingActions.SingleAsync();
        stored.ResolvedAtUtc = DateTime.UtcNow.AddMinutes(-20);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<AiConversationConflictException>(() => service.UndoActionAsync(actor, conversationId, actionId, CancellationToken.None));
    }
}
