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

/// <summary>Memoria entre turnos (referencias de herramientas) y preferencias del usuario.</summary>
public sealed class AiMemoryAndPreferencesTests
{
    private static readonly Guid PortalId = Guid.Parse("3f2a7c1e-0000-4000-8000-000000000001");
    private static readonly Guid TaskId = Guid.Parse("3f2a7c1e-0000-4000-8000-000000000002");

    [Fact]
    public void ReferencesKeepOnlyKindNameAndId()
    {
        var data = new JsonObject
        {
            ["result"] = new JsonObject
            {
                ["project"] = new JsonObject { ["Id"] = PortalId.ToString(), ["name"] = "Portal", ["budget"] = 99999 },
                ["overdueTasks"] = new JsonArray(new JsonObject { ["Id"] = TaskId.ToString(), ["title"] = "Informe", ["daysOverdue"] = 3 }),
                ["finance"] = new JsonArray(new JsonObject { ["currency"] = "ARS", ["pending"] = 1000 })
            }
        };

        var references = AiContextReferences.Extract("projects.get", data);

        Assert.Contains(references, x => x.Kind == "Proyecto" && x.Name == "Portal" && x.Id == PortalId.ToString());
        Assert.Contains(references, x => x.Kind == "Tarea" && x.Name == "Informe");
        Assert.Equal(2, references.Count);
        var note = AiContextReferences.ToHistoryNote(AiContextReferences.Serialize(references));
        Assert.Contains($"Proyecto «Portal» = {PortalId}", note);
        Assert.DoesNotContain("99999", note);
    }

    [Fact]
    public async Task ReferencesFromATurnAreSentToTheModelInTheNextTurn()
    {
        await using var db = CreateDatabase();
        var model = new RecordingModel();
        var service = new AiConversationService(db, new ApprovedProvider(), model, new ProjectsTool(), new ActiveSession(), NullLogger<AiConversationService>.Instance);
        var actor = Actor();
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);

        await service.SendMessageAsync(actor, conversation.Id, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "¿Qué proyectos hay?" }, CancellationToken.None);
        var stored = await db.AiMessages.SingleAsync(x => x.Role == "assistant");
        Assert.Contains("Portal", stored.ContextJson);

        await service.SendMessageAsync(actor, conversation.Id, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Creá una tarea en ese proyecto" }, CancellationToken.None);

        Assert.Contains($"Proyecto «Portal» = {PortalId}", model.LastInput);
        var detail = await service.GetByIdAsync(actor, conversation.Id, CancellationToken.None);
        Assert.DoesNotContain(detail!.Messages, x => x.Content.Contains("Referencias consultadas"));
    }

    [Fact]
    public async Task PreferencesAreValidatedSavedAndInjectedInThePrompt()
    {
        await using var db = CreateDatabase();
        var service = new AiPreferencesService(db, new ActiveSession());
        var actor = Actor();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(actor, new AiUserPreferencesDto { PreferredCurrency = "EUR" }, CancellationToken.None));
        var saved = await service.UpdateAsync(actor, new AiUserPreferencesDto { PreferredCurrency = "usd", DefaultMeetingMinutes = 30, ResponseStyle = "breve", Notes = "  El portal es el proyecto Portal Acme  " }, CancellationToken.None);

        Assert.Equal("USD", saved.PreferredCurrency);
        Assert.Equal("El portal es el proyecto Portal Acme", saved.Notes);
        var prompt = AiSystemPromptBuilder.Build(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone, "Nico", [], saved);
        Assert.Contains("Mostrá primero los montos en USD", prompt);
        Assert.Contains("sus reuniones duran 30 minutos", prompt);
        Assert.Contains("respuestas muy breves", prompt);
        Assert.Contains("El portal es el proyecto Portal Acme", prompt);
        Assert.DoesNotContain("Preferencias del usuario", AiSystemPromptBuilder.Build(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone, "Nico", [], new AiUserPreferencesDto()));
    }

    private static KodvianDbContext CreateDatabase()
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiExecutionContext Actor() => new()
    {
        UserId = Guid.NewGuid(), SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([RoleNames.Administrator]), Permissions = new HashSet<string>([PermissionCodes.AiOfficeUse, PermissionCodes.ProjectsRead])
    };

    /// <summary>Primera vuelta de cada mensaje: pide projects.list; segunda: responde.</summary>
    private sealed class RecordingModel : IAiResponsesClient
    {
        private int _calls;
        public string LastInput { get; private set; } = string.Empty;
        public Task<AiModelResponseDto> CreateAsync(Guid v, JsonArray input, JsonArray tools, CancellationToken ct)
        {
            LastInput = input.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return Task.FromResult(++_calls % 2 == 1
                ? new AiModelResponseDto { Status = "completed", OutputItems = new JsonArray(new JsonObject { ["type"] = "function_call", ["name"] = "projects__list", ["call_id"] = $"c{_calls}", ["arguments"] = "{}" }) }
                : new AiModelResponseDto { Status = "completed", OutputText = "Está el proyecto Portal." });
        }
    }

    private sealed class ProjectsTool : IAiToolExecutor
    {
        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) =>
            [new AiToolDefinitionDto { Name = "projects.list", Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() } }];
        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid v, string traceId, string toolName, JsonObject arguments, CancellationToken ct) =>
            Task.FromResult(new AiToolExecutionResultDto
            {
                Data = new JsonObject { ["result"] = new JsonArray(new JsonObject { ["Id"] = PortalId.ToString(), ["Name"] = "Portal" }) },
                Source = new AiSourceReferenceDto { ToolName = toolName, Source = "test", AsOfUtc = DateTime.UtcNow }
            });
        public Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ActiveSession : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ApprovedProvider : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken c) => Task.FromResult(new AiProviderConfigurationDto { IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true, Provider = "Anthropic", Model = "m" });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest r, CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest r, CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken c) => throw new NotImplementedException();
        public Task DeleteAsync(Guid v, CancellationToken c) => throw new NotImplementedException();
    }
}
