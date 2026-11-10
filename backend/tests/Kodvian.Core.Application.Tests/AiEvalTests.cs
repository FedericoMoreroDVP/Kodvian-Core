using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Kodvian.Core.Application.Tests;

/// <summary>
/// Set de evals del asistente (AiEvals/evals.json).
/// - EvalSetIsConsistentWithTheCatalog corre siempre y no usa ningún proveedor.
/// - LiveEvalsAgainstTheRealModel solo corre a demanda contra el modelo real, con estas variables de entorno:
///   KODVIAN_AI_EVALS_PROVIDER (Anthropic u OpenAI), KODVIAN_AI_EVALS_MODEL y KODVIAN_AI_EVALS_API_KEY.
///   Ejemplo: dotnet test --filter "FullyQualifiedName~LiveEvals" (consume tokens del proveedor).
/// </summary>
public sealed class AiEvalTests(ITestOutputHelper output)
{
    private sealed record EvalCase(string Id, string Prompt, string[]? ExpectedTools, string[]? ForbiddenTools, string[]? MustMention);
    private sealed record EvalSet(string Description, EvalCase[] Cases);

    private static EvalSet Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AiEvals", "evals.json");
        return JsonSerializer.Deserialize<EvalSet>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static string[] CatalogToolNames() =>
        ((AiToolDefinitionDto[])typeof(AiToolCatalog).GetField("Definitions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Select(x => x.Name).ToArray();

    [Fact]
    public void EvalSetIsConsistentWithTheCatalog()
    {
        var set = Load();
        var tools = CatalogToolNames();

        Assert.InRange(set.Cases.Length, 20, 40);
        Assert.Equal(set.Cases.Length, set.Cases.Select(x => x.Id).Distinct().Count());
        foreach (var evalCase in set.Cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(evalCase.Prompt), evalCase.Id);
            foreach (var name in (evalCase.ExpectedTools ?? []).SelectMany(x => x.Split('|')).Concat(evalCase.ForbiddenTools ?? []))
                Assert.True(tools.Contains(name), $"{evalCase.Id}: la herramienta {name} no existe en el catálogo.");
        }
    }

    [Fact]
    public async Task LiveEvalsAgainstTheRealModel()
    {
        var provider = Environment.GetEnvironmentVariable("KODVIAN_AI_EVALS_PROVIDER");
        var model = Environment.GetEnvironmentVariable("KODVIAN_AI_EVALS_MODEL");
        var apiKey = Environment.GetEnvironmentVariable("KODVIAN_AI_EVALS_API_KEY");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine("Evals contra el modelo real omitidos: configurá KODVIAN_AI_EVALS_PROVIDER, KODVIAN_AI_EVALS_MODEL y KODVIAN_AI_EVALS_API_KEY para correrlos.");
            return;
        }

        var results = new List<(string Id, bool Passed, string Detail)>();
        foreach (var evalCase in Load().Cases)
        {
            await using var db = CreateDatabase();
            var actor = Seed(db);
            var dataProtection = new EphemeralDataProtectionProvider();
            var settings = new AiProviderConfiguration
            {
                Provider = provider, Model = model, ApiKeyLastFour = "eval", IsConnectionVerified = true,
                ProtectedApiKey = dataProtection.CreateProtector("Kodvian.Core.AiProviderApiKey.v1").Protect(apiKey),
                LiveDataApprovalTermsVersion = AiPrivacyPolicy.CurrentTermsVersion, LiveDataApprovedByUserId = actor.UserId, LiveDataApprovedAtUtc = DateTime.UtcNow,
                LiveDataApprovedProvider = provider, LiveDataApprovedModel = model
            };
            db.AiProviderConfigurations.Add(settings);
            await db.SaveChangesAsync();

            IAiResponsesClient client = provider == AiProviders.Anthropic
                ? new AnthropicMessagesClient(new HttpClient { Timeout = TimeSpan.FromSeconds(150) }, db, dataProtection)
                : new OpenAiResponsesClient(new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/"), Timeout = TimeSpan.FromSeconds(120) }, db, dataProtection);
            var approved = new ApprovedProvider(settings.Version, provider, model);
            var recorder = new RecordingExecutor(CreateCatalog(db, approved));
            var service = new AiConversationService(db, approved, client, recorder, new ActiveSession(), NullLogger<AiConversationService>.Instance);
            var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);

            string answer;
            try
            {
                answer = (await service.SendMessageAsync(actor, conversation.Id, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = evalCase.Prompt }, CancellationToken.None)).AssistantMessage.Content;
            }
            catch (Exception exception)
            {
                results.Add((evalCase.Id, false, $"error: {exception.GetType().Name}: {exception.Message}"));
                continue;
            }

            var problems = new List<string>();
            foreach (var group in evalCase.ExpectedTools ?? [])
                if (!group.Split('|').Any(recorder.Called.Contains)) problems.Add($"no usó {group}");
            foreach (var forbidden in evalCase.ForbiddenTools ?? [])
                if (recorder.Called.Contains(forbidden)) problems.Add($"usó {forbidden}");
            foreach (var word in evalCase.MustMention ?? [])
                if (!answer.Contains(word, StringComparison.OrdinalIgnoreCase)) problems.Add($"no mencionó «{word}»");
            results.Add((evalCase.Id, problems.Count == 0, $"herramientas: {string.Join(", ", recorder.Called)}" + (problems.Count > 0 ? " | " + string.Join("; ", problems) : string.Empty)));
        }

        foreach (var (id, passed, detail) in results) output.WriteLine($"{(passed ? "OK  " : "FALLA")} {id}: {detail}");
        var rate = results.Count(x => x.Passed) / (double)results.Count;
        output.WriteLine($"Resultado: {results.Count(x => x.Passed)} de {results.Count} ({rate:P0}).");
        Assert.True(rate >= 0.8, $"Pasaron {rate:P0} de los evals; el mínimo es 80%.");
    }

    private static AiExecutionContext Seed(KodvianDbContext db)
    {
        var actor = new AiExecutionContext
        {
            UserId = Guid.NewGuid(), SessionVersion = Guid.NewGuid(),
            Roles = new HashSet<string>([RoleNames.Administrator]), Permissions = new HashSet<string>(RolePermissionMap.GetPermissions(RoleNames.Administrator))
        };
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone));
        var acme = new Client { CommercialName = "Acme", Status = ClientStatus.Activo };
        var portal = new Project { ClienteId = acme.Id, Nombre = "Portal Acme", Estado = ProjectStatus.EnCurso, PorcentajeAvance = 40, FechaEntregaEstimada = today.AddDays(10) };
        var income = new FinancialCategory { Name = "Servicios", MovementType = FinancialMovementType.Ingreso };
        var hosting = new FinancialCategory { Name = "Hosting", MovementType = FinancialMovementType.Egreso };
        var juana = new User { FullName = "Juana Pérez", Email = "juana@example.test" };
        db.AddRange(acme, portal, income, hosting, juana, new User { Id = actor.UserId, FullName = "Admin Evals", Email = "admin@example.test" });
        db.Tasks.Add(new TaskItem { Titulo = "Informe mensual", ProyectoId = portal.Id, CreadoPorId = actor.UserId, FechaVencimiento = today.AddDays(-3) });
        db.Tasks.Add(new TaskItem { Titulo = "Deploy a producción", ProyectoId = portal.Id, CreadoPorId = actor.UserId, FechaVencimiento = today.AddDays(2) });
        var meetingStart = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(new TimeOnly(11, 0)), AiSystemPromptBuilder.BuenosAiresTimeZone);
        db.Meetings.Add(new Meeting { ProyectoId = portal.Id, CreadoPorId = actor.UserId, Titulo = "Revisión semanal", Inicio = meetingStart, Fin = meetingStart.AddHours(1) });
        db.FinancialMovements.AddRange(
            new FinancialMovement { CategoryId = income.Id, ClientId = acme.Id, ProjectId = portal.Id, MovementType = FinancialMovementType.Ingreso, Amount = 150000, Currency = "ARS", Status = FinancialMovementStatus.Pendiente, MovementDate = today.AddDays(-20), DueDate = today.AddDays(-5), Description = "Cuota Portal", CreatedById = actor.UserId },
            new FinancialMovement { CategoryId = hosting.Id, MovementType = FinancialMovementType.Egreso, Amount = 40, Currency = "USD", Status = FinancialMovementStatus.Pagado, MovementDate = today.AddDays(-4), SettlementDate = today.AddDays(-4), Description = "Servidor", CreatedById = actor.UserId });
        db.SaveChanges();
        return actor;
    }

    private static KodvianDbContext CreateDatabase()
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiToolCatalog CreateCatalog(KodvianDbContext db, IAiProviderConfigurationService provider)
    {
        var finance = new FinanceOverviewService(db, new EvalCurrentUser());
        var storage = new NullFileStorage();
        return new AiToolCatalog(new ClientService(db), new ProjectService(db, storage, Options.Create(new StorageOptions())),
            new TaskService(db, storage, NullLogger<TaskService>.Instance), new MeetingService(db), finance, new DashboardService(db, finance),
            new FinancialMovementService(db, storage, Options.Create(new StorageOptions()), finance), provider, new ActiveSession(), db);
    }

    private sealed class RecordingExecutor(IAiToolExecutor inner) : IAiToolExecutor
    {
        public List<string> Called { get; } = [];
        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) => inner.GetAvailableTools(actor);
        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid version, string traceId, string toolName, JsonObject arguments, CancellationToken ct)
        {
            Called.Add(toolName);
            return inner.ExecuteAsync(actor, runId, version, traceId, toolName, arguments, ct);
        }
        public Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken ct) =>
            inner.ExecuteConfirmedActionAsync(actor, actionId, toolName, arguments, ct);
    }

    private sealed class ApprovedProvider(Guid version, string provider, string model) : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken c) => Task.FromResult(new AiProviderConfigurationDto
        {
            IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true, Provider = provider, Model = model, Version = version
        });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest r, CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest r, CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken c) => throw new NotImplementedException();
        public Task DeleteAsync(Guid v, CancellationToken c) => throw new NotImplementedException();
    }

    private sealed class ActiveSession : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class EvalCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? SessionVersion { get; } = Guid.NewGuid();
    }

    private sealed class NullFileStorage : IFileStorageService
    {
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
