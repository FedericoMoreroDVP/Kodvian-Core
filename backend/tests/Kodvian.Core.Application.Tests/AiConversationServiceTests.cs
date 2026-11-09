using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

public sealed class AiConversationServiceTests
{
    [Fact]
    public async Task ConversationsAreOwnerScopedAndManualDeletePreservesSaneAudit()
    {
        await using var db = CreateDatabase();
        var model = new FakeResponsesClient();
        var service = CreateService(db, model, new FakeProviderConfiguration { IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true });
        var owner = Actor();
        var other = Actor();
        var conversation = await service.CreateAsync(owner, new AiConversationCreateRequest(), CancellationToken.None);

        Assert.Null(await service.GetByIdAsync(other, conversation.Id, CancellationToken.None));
        var result = await service.SendMessageAsync(owner, conversation.Id, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "¿Cómo va el proyecto?" }, CancellationToken.None);
        Assert.Contains("fuente autorizada", result.AssistantMessage.Content);
        Assert.Equal(1, model.Calls);

        var replay = await service.SendMessageAsync(owner, conversation.Id, new AiSendMessageRequest
        {
            ClientRequestId = await db.AiRuns.Select(x => x.ClientRequestId).SingleAsync(),
            Content = "¿Cómo va el proyecto?"
        }, CancellationToken.None);
        Assert.Equal(result.RunId, replay.RunId);
        Assert.Equal(1, model.Calls);

        Assert.True(await service.DeleteAsync(owner, conversation.Id, CancellationToken.None));
        Assert.Empty(await db.AiConversations.ToListAsync());
        Assert.Empty(await db.AiMessages.ToListAsync());
        Assert.Empty(await db.AiRuns.ToListAsync());
        Assert.Contains(await db.AiAuditEvents.ToListAsync(), x => x.Decision == "Deleted" && x.ReasonCode == "owner_delete");
    }

    [Fact]
    public async Task LiveConversationIsBlockedUntilAdministratorApproval()
    {
        await using var db = CreateDatabase();
        var service = CreateService(db, new FakeResponsesClient(), new FakeProviderConfiguration { IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = false });
        var actor = Actor();
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<AiLiveDataApprovalRequiredException>(() => service.SendMessageAsync(actor, conversation.Id,
            new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Consulta" }, CancellationToken.None));
        Assert.Empty(await db.AiMessages.ToListAsync());
        Assert.Empty(await db.AiRuns.ToListAsync());
    }

    [Fact]
    public async Task ToolCallRunsThroughCatalogAndPersistsOnlySourceMetadataWithAssistantMessage()
    {
        await using var db = CreateDatabase();
        var model = new FunctionCallResponsesClient();
        var service = new AiConversationService(db, new FakeProviderConfiguration { IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true },
            model, new OneToolExecutor(), new ActiveSessionValidator(), NullLogger<AiConversationService>.Instance);
        var actor = Actor();
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);

        var result = await service.SendMessageAsync(actor, conversation.Id,
            new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Consulta confirmada" }, CancellationToken.None);

        Assert.Equal(2, model.Calls);
        Assert.All(model.ToolNames, name => Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", name));
        Assert.Contains("function_call_output", model.SecondInput);
        Assert.Single(result.AssistantMessage.Sources);
        Assert.Equal("safe.read", result.AssistantMessage.Sources.Single().ToolName);
        Assert.Equal(1, (await db.AiRuns.SingleAsync()).ToolCallCount);
    }

    private static KodvianDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new KodvianDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiExecutionContext Actor() => new()
    {
        UserId = Guid.NewGuid(),
        SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([Kodvian.Core.Application.Common.Security.RoleNames.Administrator], StringComparer.Ordinal),
        Permissions = new HashSet<string>([Kodvian.Core.Application.Common.Security.PermissionCodes.AiOfficeUse], StringComparer.Ordinal)
    };

    private static AiConversationService CreateService(KodvianDbContext db, FakeResponsesClient client, FakeProviderConfiguration provider) =>
        new(db, provider, client, new FakeToolExecutor(), new ActiveSessionValidator(), NullLogger<AiConversationService>.Instance);

    private sealed class ActiveSessionValidator : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeProviderConfiguration : IAiProviderConfigurationService
    {
        public bool IsConfigured { get; init; }
        public bool IsConnectionVerified { get; init; }
        public bool LiveDataApproved { get; init; }
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto
        {
            IsConfigured = IsConfigured, IsConnectionVerified = IsConnectionVerified, LiveDataApproved = LiveDataApproved,
            Provider = "OpenAI", Model = "model-test"
        });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeResponsesClient : IAiResponsesClient
    {
        public int Calls { get; private set; }
        public Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AiModelResponseDto { Status = "completed", OutputText = "Respuesta de prueba" });
        }
    }

    private sealed class FakeToolExecutor : IAiToolExecutor
    {
        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) => [];
        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid expectedConfigurationVersion, string traceId, string toolName, JsonObject arguments, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FunctionCallResponsesClient : IAiResponsesClient
    {
        public int Calls { get; private set; }
        public string SecondInput { get; private set; } = string.Empty;
        public List<string> ToolNames { get; } = [];
        public Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                ToolNames.AddRange(tools.Select(x => x!["name"]!.GetValue<string>()));
                return Task.FromResult(new AiModelResponseDto
                {
                    Status = "completed",
                    OutputItems = new JsonArray(new JsonObject
                    {
                        ["type"] = "function_call", ["name"] = ToolNames.Single(), ["call_id"] = "call-1", ["arguments"] = "{}"
                    })
                });
            }
            SecondInput = input.ToJsonString();
            return Task.FromResult(new AiModelResponseDto { Status = "completed", OutputText = "Dato confirmado." });
        }
    }

    private sealed class OneToolExecutor : IAiToolExecutor
    {
        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) =>
            [new AiToolDefinitionDto { Name = "safe.read", Description = "Lectura de prueba", Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() } }];

        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid expectedConfigurationVersion, string traceId, string toolName, JsonObject arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new AiToolExecutionResultDto
            {
                Data = new JsonObject { ["result"] = new JsonObject { ["count"] = 3 }, ["traceId"] = traceId },
                Source = new AiSourceReferenceDto { ToolName = toolName, Source = "test.source", AsOfUtc = DateTime.UtcNow }
            });
    }
}
