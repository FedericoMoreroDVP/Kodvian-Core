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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

public sealed class AiToolCatalogTests
{
    [Fact]
    public async Task ClientToolOmitsContactTaxFinancialAndNotesData()
    {
        await using var db = CreateDatabase();
        db.Clients.Add(new Client
        {
            CommercialName = "Cliente visible",
            LegalName = "Razón Legal Privada",
            TaxId = "30-12345678-9",
            ContactName = "Contacto Privado",
            ContactEmail = "private@example.test",
            ContactPhone = "+54 11 5555 1111",
            Address = "Dirección privada",
            MonthlyAmount = 123456,
            Notes = "Nota confidencial"
        });
        db.SaveChanges();

        var catalog = CreateCatalog(db);
        var actor = AdminActor();
        var result = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace-test", "clients.list", new JsonObject(), CancellationToken.None);
        var json = result.Data.ToJsonString();

        Assert.Contains("Cliente visible", json);
        Assert.DoesNotContain("Razón Legal Privada", json);
        Assert.DoesNotContain("30-12345678-9", json);
        Assert.DoesNotContain("Contacto Privado", json);
        Assert.DoesNotContain("private@example.test", json);
        Assert.DoesNotContain("5555 1111", json);
        Assert.DoesNotContain("Dirección privada", json);
        Assert.DoesNotContain("123456", json);
        Assert.DoesNotContain("Nota confidencial", json);
    }

    [Fact]
    public async Task AdministratorGetsWriteToolsMarkedAsRequiringConfirmation()
    {
        await using var db = CreateDatabase();
        var tools = CreateCatalog(db).GetAvailableTools(AdminActor());

        Assert.True(tools.Single(x => x.Name == "tasks.create").RequiresConfirmation);
        Assert.True(tools.Single(x => x.Name == "tasks.update_status").RequiresConfirmation);
        Assert.True(tools.Single(x => x.Name == "meetings.create").RequiresConfirmation);
        Assert.False(tools.Single(x => x.Name == "people.list").RequiresConfirmation);
    }

    [Fact]
    public async Task TaskCreateOnlyProposesAndConfirmedExecutionCreatesTheTask()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var developer = new Developer { FullName = "Juana Dev" };
        db.Developers.Add(developer);
        db.SaveChanges();
        var catalog = CreateCatalog(db);
        var arguments = new JsonObject
        {
            ["projectId"] = project.Id.ToString(), ["title"] = "Llamar a Juan", ["dueDate"] = "2026-11-11", ["priority"] = "alta", ["developerId"] = developer.Id.ToString()
        };

        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "tasks.create", arguments, CancellationToken.None);

        Assert.Empty(db.Tasks);
        Assert.NotNull(proposal.Proposal);
        Assert.Null(proposal.Source);
        Assert.Equal("pending_confirmation", proposal.Data["status"]!.GetValue<string>());
        Assert.Contains(proposal.Proposal!.Details, x => x.Label == "Vence" && x.Value == "11/11/2026");
        Assert.Contains(proposal.Proposal.Details, x => x.Label == "Desarrollador" && x.Value == "Juana Dev");
        Assert.Equal("Alta", proposal.Proposal.Arguments["priority"]!.GetValue<string>());
        Assert.Contains(db.AiAuditEvents, x => x.Decision == "Proposed");

        var outcome = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "tasks.create", proposal.Proposal.Arguments, CancellationToken.None);

        var task = Assert.Single(db.Tasks);
        Assert.Equal("Llamar a Juan", task.Titulo);
        Assert.Equal(project.Id, task.ProyectoId);
        Assert.Equal(new DateOnly(2026, 11, 11), task.FechaVencimiento);
        Assert.Equal(TaskPriority.Alta, task.Prioridad);
        Assert.Equal(developer.Id, task.DeveloperId);
        Assert.Equal(actor.UserId, task.CreadoPorId);
        Assert.Equal("/tareas", outcome.Link);
        Assert.Contains("Llamar a Juan", outcome.Message);
    }

    [Fact]
    public async Task TaskCreateWithUnknownProjectIsRejectedForTheModel()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);

        var error = await Assert.ThrowsAsync<AiToolRejectedException>(() => CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "tasks.create",
            new JsonObject { ["projectId"] = Guid.NewGuid().ToString(), ["title"] = "X" }, CancellationToken.None));

        Assert.Contains("projects.list", error.Message);
    }

    [Fact]
    public async Task ConfirmedActionRequiresWritePermission()
    {
        await using var db = CreateDatabase();
        var (admin, project) = SeedProject(db);
        var readOnly = new AiExecutionContext
        {
            UserId = admin.UserId, SessionVersion = admin.SessionVersion, Roles = admin.Roles,
            Permissions = new HashSet<string>([PermissionCodes.AiOfficeUse, PermissionCodes.TasksRead], StringComparer.Ordinal)
        };

        await Assert.ThrowsAsync<AiToolRejectedException>(() => CreateCatalog(db).ExecuteConfirmedActionAsync(readOnly, Guid.NewGuid(), "tasks.create",
            new JsonObject { ["projectId"] = project.Id.ToString(), ["title"] = "X" }, CancellationToken.None));
        Assert.Empty(db.Tasks);
    }

    [Fact]
    public async Task TaskStatusChangeIsProposedAndAppliedOnConfirm()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var task = new TaskItem { Titulo = "Revisar contrato", ProyectoId = project.Id, CreadoPorId = actor.UserId, OrdenKanban = 4 };
        db.Tasks.Add(task);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "tasks.update_status",
            new JsonObject { ["taskId"] = task.Id.ToString(), ["status"] = "Finalizada" }, CancellationToken.None);
        Assert.Equal(Kodvian.Core.Domain.Enums.TaskStatus.Pendiente, db.Tasks.AsNoTracking().Single().Estado);
        Assert.Contains("de Pendiente a Finalizada", proposal.Proposal!.Summary);

        await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "tasks.update_status", proposal.Proposal.Arguments, CancellationToken.None);

        var updated = db.Tasks.AsNoTracking().Single();
        Assert.Equal(Kodvian.Core.Domain.Enums.TaskStatus.Finalizada, updated.Estado);
        Assert.Equal(4, updated.OrdenKanban);
    }

    [Fact]
    public async Task MeetingCreateUsesBuenosAiresLocalTimeAndParticipants()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var participant = new User { FullName = "Pedro Participante", Email = "pedro@example.test" };
        db.Users.Add(participant);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "meetings.create", new JsonObject
        {
            ["projectId"] = project.Id.ToString(), ["title"] = "Kickoff", ["startsAt"] = "2026-11-12T10:00", ["durationMinutes"] = 45,
            ["participantIds"] = new JsonArray(participant.Id.ToString())
        }, CancellationToken.None);

        Assert.Empty(db.Meetings);
        Assert.Equal("2026-11-12T10:45", proposal.Proposal!.Arguments["endsAt"]!.GetValue<string>());
        Assert.Contains(proposal.Proposal.Details, x => x.Label == "Participantes" && x.Value == "Pedro Participante");

        await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "meetings.create", proposal.Proposal.Arguments, CancellationToken.None);

        var meeting = db.Meetings.Include(x => x.Participantes).Single();
        Assert.Equal(new DateTime(2026, 11, 12, 13, 0, 0, DateTimeKind.Utc), meeting.Inicio);
        Assert.Equal(new DateTime(2026, 11, 12, 13, 45, 0, DateTimeKind.Utc), meeting.Fin);
        Assert.Contains(meeting.Participantes, x => x.UserId == participant.Id);
    }

    [Fact]
    public async Task PeopleListFindsUsersAndDevelopersByName()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);
        db.Users.Add(new User { FullName = "Juan Pérez", Email = "juan@example.test" });
        db.Users.Add(new User { FullName = "Inactivo Juan", Email = "old@example.test", Activo = false });
        db.Developers.Add(new Developer { FullName = "Juan Dev" });
        db.Developers.Add(new Developer { FullName = "Otra Persona" });
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "people.list", new JsonObject { ["search"] = "juan" }, CancellationToken.None);
        var output = result.Data["result"]!;

        Assert.Single(output["users"]!.AsArray());
        Assert.Single(output["developers"]!.AsArray());
        Assert.Equal("Juan Dev", output["developers"]![0]!["Name"]!.GetValue<string>());
    }

    private static (AiExecutionContext Actor, Project Project) SeedProject(KodvianDbContext db)
    {
        var actor = AdminActor();
        var client = new Client { CommercialName = "Cliente" };
        var project = new Project { ClienteId = client.Id, Nombre = "Proyecto Demo" };
        db.Clients.Add(client);
        db.Projects.Add(project);
        db.Users.Add(new User { Id = actor.UserId, FullName = "Admin", Email = "admin@example.test" });
        db.SaveChanges();
        return (actor, project);
    }

    [Fact]
    public async Task MeetingsWithoutDatesReturnTheNextSevenDays()
    {
        await using var db = CreateDatabase();
        var actor = AdminActor();
        var client = new Client { CommercialName = "Cliente" };
        var project = new Project { ClienteId = client.Id, Nombre = "Proyecto" };
        db.Clients.Add(client);
        db.Projects.Add(project);
        db.Users.Add(new User { Id = actor.UserId, FullName = "Admin", Email = "admin@example.test" });
        db.Meetings.AddRange(
            new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Reunion proxima", Inicio = DateTime.UtcNow.AddDays(1), Fin = DateTime.UtcNow.AddDays(1).AddHours(1) },
            new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Reunion pasada", Inicio = DateTime.UtcNow.AddDays(-10), Fin = DateTime.UtcNow.AddDays(-10).AddHours(1) },
            new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Reunion lejana", Inicio = DateTime.UtcNow.AddDays(20), Fin = DateTime.UtcNow.AddDays(20).AddHours(1) });
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "meetings.list", new JsonObject(), CancellationToken.None);
        var json = result.Data.ToJsonString();

        Assert.Contains("Reunion proxima", json);
        Assert.DoesNotContain("Reunion pasada", json);
        Assert.DoesNotContain("Reunion lejana", json);
    }

    [Fact]
    public async Task ToolsRequireAdminAiPermissionAndCurrentSession()
    {
        await using var db = CreateDatabase();
        var catalog = CreateCatalog(db);
        var actor = AdminActor();
        Assert.Contains(catalog.GetAvailableTools(actor), x => x.Name == "finance.get_summary");

        var noAiPermission = new AiExecutionContext
        {
            UserId = actor.UserId, SessionVersion = actor.SessionVersion, Roles = actor.Roles,
            Permissions = new HashSet<string>([PermissionCodes.ClientsRead], StringComparer.Ordinal)
        };
        Assert.Empty(catalog.GetAvailableTools(noAiPermission));

        var noLongerAdmin = new AiExecutionContext
        {
            UserId = actor.UserId, SessionVersion = actor.SessionVersion,
            Roles = new HashSet<string>([RoleNames.Analyst], StringComparer.Ordinal),
            Permissions = actor.Permissions
        };
        Assert.Empty(catalog.GetAvailableTools(noLongerAdmin));
    }

    private static KodvianDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new KodvianDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiExecutionContext AdminActor() => new()
    {
        UserId = Guid.NewGuid(),
        SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([RoleNames.Administrator], StringComparer.Ordinal),
        Permissions = new HashSet<string>(RolePermissionMap.GetPermissions(RoleNames.Administrator), StringComparer.Ordinal)
    };

    private static AiToolCatalog CreateCatalog(KodvianDbContext db)
    {
        var currentUser = new TestCurrentUser();
        var finance = new FinanceOverviewService(db, currentUser);
        return new AiToolCatalog(
            new ClientService(db),
            new ProjectService(db, new NullFileStorage(), Options.Create(new StorageOptions())),
            new TaskService(db, new NullFileStorage(), NullLogger<TaskService>.Instance),
            new MeetingService(db),
            finance,
            new DashboardService(db, finance),
            new FakeProviderConfiguration(),
            new ActiveSessionValidator(),
            db);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? SessionVersion { get; } = Guid.NewGuid();
    }

    private sealed class ActiveSessionValidator : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeProviderConfiguration : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto
        {
            IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true
        });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class NullFileStorage : IFileStorageService
    {
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
