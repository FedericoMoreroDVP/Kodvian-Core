using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

/// <summary>Costos estimados, tope mensual, modelo según la tarea y recuperación de acciones trabadas.</summary>
public sealed class AiUsageControlsTests
{
    [Fact]
    public void CostUsesTheTierPricesAndNeedsPrices()
    {
        var settings = new AiUsageSettings { InputUsdPerMillion = 3, OutputUsdPerMillion = 15, FastInputUsdPerMillion = 1, FastOutputUsdPerMillion = 5 };

        Assert.Equal(0.0105m, AiUsageService.EstimateCost(settings, AiModelTier.Main, 1000, 500));
        Assert.Equal(0.0035m, AiUsageService.EstimateCost(settings, AiModelTier.Fast, 1000, 500));
        Assert.Null(AiUsageService.EstimateCost(new AiUsageSettings(), AiModelTier.Main, 1000, 500));
        Assert.Null(AiUsageService.EstimateCost(null, AiModelTier.Main, 1000, 500));
    }

    [Theory]
    [InlineData("¿Qué tareas vencen hoy?", true, AiModelTier.Fast)]
    [InlineData("¿Quién me debe?", true, AiModelTier.Fast)]
    [InlineData("Agendá una reunión el jueves", true, AiModelTier.Main)]
    [InlineData("Compará los gastos con el mes pasado", true, AiModelTier.Main)]
    [InlineData("¿Por qué bajaron los ingresos?", true, AiModelTier.Main)]
    [InlineData("¿Qué tareas vencen hoy?", false, AiModelTier.Main)]
    public void SimpleQuestionsGoToTheFastModelOnlyWhenConfigured(string message, bool fastConfigured, AiModelTier expected) =>
        Assert.Equal(expected, AiModelSelection.Choose(message, fastConfigured));

    [Fact]
    public void ResolvedModelFollowsTheCurrentTier()
    {
        Assert.Equal("principal", AiModelSelection.Resolve("principal", "rapido"));
        using (AiModelSelection.Use(AiModelTier.Fast))
        {
            Assert.Equal("rapido", AiModelSelection.Resolve("principal", "rapido"));
            Assert.Equal("principal", AiModelSelection.Resolve("principal", null));
        }
        Assert.Equal("principal", AiModelSelection.Resolve("principal", "rapido"));
    }

    [Fact]
    public async Task BudgetLevelsAndReport()
    {
        await using var db = CreateDatabase();
        var service = new AiUsageService(db);
        await service.UpdateSettingsAsync(Guid.NewGuid(), new AiUsageSettingsDto { InputUsdPerMillion = 3, OutputUsdPerMillion = 15, MonthlyBudgetUsd = 10 }, CancellationToken.None);
        var conversation = new AiConversation { OwnerUserId = Guid.NewGuid(), Title = "x" };
        db.AiConversations.Add(conversation);
        db.AiRuns.Add(new AiRun { ConversationId = conversation.Id, ActorUserId = conversation.OwnerUserId, ClientRequestId = Guid.NewGuid(), Status = "Completed", Provider = "Anthropic", Model = "m", InputTokens = 1000, OutputTokens = 100, EstimatedCostUsd = 8.5m, StartedAtUtc = DateTime.UtcNow });
        db.AiAuditEvents.Add(new AiAuditEvent { ActorUserId = conversation.OwnerUserId, ToolName = "tasks.list", Decision = "Allowed", ReasonCode = "completed", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var budget = await service.GetBudgetStatusAsync(CancellationToken.None);
        Assert.Equal("warning", budget.Level);
        Assert.Equal(85m, budget.UsedPercent);

        var report = await service.GetReportAsync(7, CancellationToken.None);
        Assert.Equal(7, report.Days.Count);
        Assert.Equal(1, report.Totals.Runs);
        Assert.Equal(8.5m, report.Totals.CostUsd);
        Assert.Equal("tasks.list", report.TopTools.Single().Name);
        Assert.Equal(1, report.ActiveUsers);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetReportAsync(120, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSettingsAsync(Guid.NewGuid(), new AiUsageSettingsDto { InputUsdPerMillion = -1 }, CancellationToken.None));
    }

    [Fact]
    public async Task ExceededBudgetPausesTheAssistantAndRunsRecordTheirCost()
    {
        await using var db = CreateDatabase();
        db.AiUsageSettings.Add(new AiUsageSettings { InputUsdPerMillion = 1000, OutputUsdPerMillion = 1000, MonthlyBudgetUsd = 1 });
        await db.SaveChangesAsync();
        var service = new AiConversationService(db, new ApprovedProvider(), new TextModel(), new NoTools(), new ActiveSession(), NullLogger<AiConversationService>.Instance);
        var actor = Actor();
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);

        await service.SendMessageAsync(actor, conversation.Id, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Hola" }, CancellationToken.None);
        var run = await db.AiRuns.SingleAsync();
        Assert.Equal(1.1m, run.EstimatedCostUsd); // (1000 + 100) tokens a USD 1000 por millón

        var error = await Assert.ThrowsAsync<AiProviderUnavailableException>(() => service.SendMessageAsync(actor, conversation.Id,
            new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "Otra" }, CancellationToken.None));
        Assert.Contains("tope mensual", error.Message);
    }

    [Fact]
    public async Task ActionsStuckInExecutingAreMarkedAsNotCompleted()
    {
        await using var db = CreateDatabase();
        var service = new AiConversationService(db, new ApprovedProvider(), new TextModel(), new NoTools(), new ActiveSession(), NullLogger<AiConversationService>.Instance);
        var actor = Actor();
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);
        db.AiPendingActions.Add(new AiPendingAction
        {
            ConversationId = conversation.Id, ActorUserId = actor.UserId, ToolName = "tasks.create", Title = "Crear tarea", Summary = "x",
            Status = "Executing", ExecutionStartedAtUtc = DateTime.UtcNow.AddMinutes(-10), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        await service.GetByIdAsync(actor, conversation.Id, CancellationToken.None);

        var action = await db.AiPendingActions.AsNoTracking().SingleAsync();
        Assert.Equal("Failed", action.Status);
        Assert.Contains("Revisá", action.ResultMessage);
        Assert.Contains(await db.AiAuditEvents.ToListAsync(), x => x.ReasonCode == "stale_action_recovered");
    }

    [Fact]
    public async Task ChangingTheFastModelRequiresApprovingLiveDataAgain()
    {
        await using var db = CreateDatabase();
        var settings = new AiProviderConfiguration
        {
            Provider = AiProviders.Anthropic, Model = "principal", ProtectedApiKey = "x", ApiKeyLastFour = "1234", IsConnectionVerified = true,
            LiveDataApprovalTermsVersion = AiPrivacyPolicy.CurrentTermsVersion, LiveDataApprovedByUserId = Guid.NewGuid(), LiveDataApprovedAtUtc = DateTime.UtcNow,
            LiveDataApprovedProvider = AiProviders.Anthropic, LiveDataApprovedModel = "principal"
        };
        db.AiProviderConfigurations.Add(settings);
        await db.SaveChangesAsync();
        var dataProtection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        settings.ProtectedApiKey = dataProtection.CreateProtector("Kodvian.Core.AiProviderApiKey.v1").Protect("clave");
        await db.SaveChangesAsync();
        var service = new AiProviderConfigurationService(db, dataProtection, [], new TestCurrentUser());

        var updated = await service.UpdateAsync(new AiProviderConfigurationUpdateRequest { Provider = AiProviders.Anthropic, Model = "principal", FastModel = "rapido", Version = settings.Version }, CancellationToken.None);

        Assert.Equal("rapido", updated.FastModel);
        Assert.True(updated.IsConnectionVerified);
        Assert.False(updated.LiveDataApproved);
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
        Roles = new HashSet<string>([RoleNames.Administrator]), Permissions = new HashSet<string>([PermissionCodes.AiOfficeUse])
    };

    private sealed class TextModel : IAiResponsesClient
    {
        public Task<AiModelResponseDto> CreateAsync(Guid v, JsonArray input, JsonArray tools, CancellationToken ct) =>
            Task.FromResult(new AiModelResponseDto { Status = "completed", OutputText = "Hola.", InputTokens = 1000, OutputTokens = 100 });
    }

    private sealed class NoTools : IAiToolExecutor
    {
        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) => [];
        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid v, string traceId, string toolName, JsonObject arguments, CancellationToken ct) => throw new NotImplementedException();
        public Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ActiveSession : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? SessionVersion { get; } = Guid.NewGuid();
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
