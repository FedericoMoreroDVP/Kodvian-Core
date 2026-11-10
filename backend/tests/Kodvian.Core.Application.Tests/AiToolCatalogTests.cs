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
    public async Task TaskUpdateReschedulesAndCanBeUndoneUnlessSomeoneChangedIt()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var task = new TaskItem { Titulo = "Informe", ProyectoId = project.Id, CreadoPorId = actor.UserId, FechaVencimiento = new DateOnly(2026, 11, 12) };
        db.Tasks.Add(task);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "tasks.update",
            new JsonObject { ["taskId"] = task.Id.ToString(), ["dueDate"] = "2026-11-20", ["priority"] = "Alta" }, CancellationToken.None);
        Assert.Equal("Reprogramar tarea", proposal.Proposal!.Title);
        Assert.Contains(proposal.Proposal.Details, x => x.Label == "Vence" && x.Value == "12/11/2026 → 20/11/2026");

        var outcome = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "tasks.update", proposal.Proposal.Arguments, CancellationToken.None);
        var updated = db.Tasks.AsNoTracking().Single();
        Assert.Equal(new DateOnly(2026, 11, 20), updated.FechaVencimiento);
        Assert.Equal(TaskPriority.Alta, updated.Prioridad);
        Assert.NotNull(outcome.UndoData);

        await catalog.UndoActionAsync(actor, Guid.NewGuid(), "tasks.update", task.Id, outcome.UndoData!, CancellationToken.None);
        var reverted = db.Tasks.AsNoTracking().Single();
        Assert.Equal(new DateOnly(2026, 11, 12), reverted.FechaVencimiento);
        Assert.Equal(TaskPriority.Media, reverted.Prioridad);

        // Si alguien la cambia después, deshacer ya no es seguro.
        var second = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "tasks.update", proposal.Proposal.Arguments, CancellationToken.None);
        var tracked = db.Tasks.Single();
        tracked.Titulo = "Informe editado a mano";
        db.SaveChanges();
        var error = await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.UndoActionAsync(actor, Guid.NewGuid(), "tasks.update", task.Id, second.UndoData!, CancellationToken.None));
        Assert.Contains("modificó", error.Message);
    }

    [Fact]
    public async Task UndoingATaskCreationRemovesTheTask()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var catalog = CreateCatalog(db);
        var outcome = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "tasks.create",
            new JsonObject { ["projectId"] = project.Id.ToString(), ["title"] = "Temporal" }, CancellationToken.None);
        Assert.Single(db.Tasks);

        await catalog.UndoActionAsync(actor, Guid.NewGuid(), "tasks.create", outcome.EntityId!.Value, outcome.UndoData!, CancellationToken.None);

        Assert.Empty(db.Tasks.AsNoTracking());
    }

    [Fact]
    public async Task StatusChangeUndoRestoresThePreviousStatus()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var task = new TaskItem { Titulo = "Deploy", ProyectoId = project.Id, CreadoPorId = actor.UserId, Estado = Kodvian.Core.Domain.Enums.TaskStatus.EnCurso };
        db.Tasks.Add(task);
        db.SaveChanges();
        var catalog = CreateCatalog(db);
        var outcome = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "tasks.update_status",
            new JsonObject { ["taskId"] = task.Id.ToString(), ["status"] = "Finalizada" }, CancellationToken.None);

        await catalog.UndoActionAsync(actor, Guid.NewGuid(), "tasks.update_status", task.Id, outcome.UndoData!, CancellationToken.None);

        Assert.Equal(Kodvian.Core.Domain.Enums.TaskStatus.EnCurso, db.Tasks.AsNoTracking().Single().Estado);
    }

    [Fact]
    public async Task MeetingCanBeMovedKeepingItsDurationUndoneAndCancelled()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var start = new DateTime(2026, 11, 12, 13, 0, 0, DateTimeKind.Utc); // 10:00 en Buenos Aires
        var meeting = new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Revisión", Inicio = start, Fin = start.AddMinutes(45) };
        db.Meetings.Add(meeting);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "meetings.update",
            new JsonObject { ["meetingId"] = meeting.Id.ToString(), ["startsAt"] = "2026-11-13T15:00" }, CancellationToken.None);
        Assert.Equal("2026-11-13T15:45", proposal.Proposal!.Arguments["endsAt"]!.GetValue<string>());
        var outcome = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "meetings.update", proposal.Proposal.Arguments, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc), db.Meetings.AsNoTracking().Single().Inicio);

        await catalog.UndoActionAsync(actor, Guid.NewGuid(), "meetings.update", meeting.Id, outcome.UndoData!, CancellationToken.None);
        Assert.Equal(start, db.Meetings.AsNoTracking().Single().Inicio);

        var cancel = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "meetings.cancel", new JsonObject { ["meetingId"] = meeting.Id.ToString() }, CancellationToken.None);
        var cancelled = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "meetings.cancel", cancel.Proposal!.Arguments, CancellationToken.None);
        Assert.Equal(MeetingStatus.Cancelada, db.Meetings.AsNoTracking().Single().Estado);
        Assert.Null(cancelled.UndoData);
    }

    [Fact]
    public async Task MovementRegistrationResolvesTheCategoryByNameAndIsIdempotent()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);
        db.FinancialCategories.AddRange(
            new FinancialCategory { Name = "Hosting", MovementType = FinancialMovementType.Egreso },
            new FinancialCategory { Name = "Sueldos", MovementType = FinancialMovementType.Egreso });
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var unknown = await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "finance.create_movement",
            new JsonObject { ["type"] = "Egreso", ["amount"] = 20, ["currency"] = "USD", ["description"] = "Servidor", ["category"] = "Viajes" }, CancellationToken.None));
        Assert.Contains("Hosting", unknown.Message);

        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone)).AddDays(-1);
        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "finance.create_movement", new JsonObject
        {
            ["type"] = "Egreso", ["amount"] = 20.5, ["currency"] = "USD", ["description"] = "Servidor", ["category"] = "host", ["settled"] = true, ["date"] = yesterday.ToString("yyyy-MM-dd")
        }, CancellationToken.None);
        Assert.Contains(proposal.Proposal!.Details, x => x.Label == "Categoría" && x.Value == "Hosting");

        var actionId = Guid.NewGuid();
        await catalog.ExecuteConfirmedActionAsync(actor, actionId, "finance.create_movement", proposal.Proposal.Arguments, CancellationToken.None);
        await catalog.ExecuteConfirmedActionAsync(actor, actionId, "finance.create_movement", proposal.Proposal.Arguments, CancellationToken.None);

        var movement = Assert.Single(db.FinancialMovements.AsNoTracking());
        Assert.Equal(actionId, movement.Id);
        Assert.Equal(20.5m, movement.Amount);
        Assert.Equal(FinancialMovementStatus.Pagado, movement.Status);
        Assert.Equal(yesterday, movement.SettlementDate);
    }

    [Fact]
    public async Task PreferencesCanBeSavedThroughTheAssistantAndUndone()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);
        var catalog = CreateCatalog(db);

        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "preferences.update",
            new JsonObject { ["defaultMeetingMinutes"] = 30, ["preferredCurrency"] = "USD" }, CancellationToken.None);
        Assert.Contains(proposal.Proposal!.Details, x => x.Label == "Duración de reuniones" && x.Value == "sin preferencia → 30");
        Assert.Null(db.AiUserPreferences.AsNoTracking().SingleOrDefault());

        var outcome = await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "preferences.update", proposal.Proposal.Arguments, CancellationToken.None);
        var saved = db.AiUserPreferences.AsNoTracking().Single();
        Assert.Equal(30, saved.DefaultMeetingMinutes);
        Assert.Equal("USD", saved.PreferredCurrency);

        await catalog.UndoActionAsync(actor, Guid.NewGuid(), "preferences.update", actor.UserId, outcome.UndoData!, CancellationToken.None);
        Assert.Null(db.AiUserPreferences.AsNoTracking().Single().DefaultMeetingMinutes);
    }

    [Fact]
    public async Task ProspectIsCreatedOnceAndDuplicatesAreRejected()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);
        var catalog = CreateCatalog(db);
        var proposal = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "clients.create_prospect",
            new JsonObject { ["name"] = "Nueva Empresa", ["serviceType"] = "Desarrollo web" }, CancellationToken.None);

        await catalog.ExecuteConfirmedActionAsync(actor, Guid.NewGuid(), "clients.create_prospect", proposal.Proposal!.Arguments, CancellationToken.None);

        var client = db.Clients.AsNoTracking().Single(x => x.CommercialName == "Nueva Empresa");
        Assert.Equal(ClientStatus.Prospecto, client.Status);
        await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "clients.create_prospect",
            new JsonObject { ["name"] = "nueva empresa" }, CancellationToken.None));
    }

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

    [Fact]
    public async Task PendingFinanceGroupsReceivablesByClientWithDaysOverdue()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone));
        var category = new FinancialCategory { Name = "Servicios", MovementType = FinancialMovementType.Ingreso };
        var acme = new Client { CommercialName = "Acme" };
        var beta = new Client { CommercialName = "Beta" };
        db.AddRange(category, acme, beta);
        db.FinancialMovements.AddRange(
            Movement(category, acme.Id, 1000, "ARS", FinancialMovementStatus.Pendiente, today.AddDays(-20)),
            Movement(category, acme.Id, 500, "ARS", FinancialMovementStatus.Vencido, today.AddDays(-5)),
            Movement(category, beta.Id, 300, "USD", FinancialMovementStatus.Pendiente, today.AddDays(10)),
            Movement(category, beta.Id, 999, "ARS", FinancialMovementStatus.Cobrado, today.AddDays(-30)));
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "finance.list_pending", new JsonObject { ["kind"] = "cobros" }, CancellationToken.None);
        var output = result.Data["result"]!;

        var byClient = output["receivablesByClient"]!.AsArray();
        Assert.Equal("Acme", byClient[0]!["client"]!.GetValue<string>());
        Assert.Equal(1500m, byClient[0]!["total"]!.GetValue<decimal>());
        Assert.Equal(20, byClient[0]!["maxDaysOverdue"]!.GetValue<int>());
        Assert.Equal(3, output["items"]!.AsArray().Count);
        var ars = output["totals"]!.AsArray().Single(x => x!["Currency"]!.GetValue<string>() == "ARS");
        Assert.Equal(1500m, ars!["overdue"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task MovementsCanBeSearchedAndReturnTotalsForTheWholeFilter()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone));
        var category = new FinancialCategory { Name = "Hosting", MovementType = FinancialMovementType.Egreso };
        db.Add(category);
        db.FinancialMovements.AddRange(
            Movement(category, null, 100, "USD", FinancialMovementStatus.Pagado, today.AddDays(-2), FinancialMovementType.Egreso, "Servidor AWS"),
            Movement(category, null, 50, "USD", FinancialMovementStatus.Pagado, today.AddDays(-3), FinancialMovementType.Egreso, "Dominio"),
            Movement(category, null, 70, "USD", FinancialMovementStatus.Anulado, today.AddDays(-3), FinancialMovementType.Egreso, "Anulado"));
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "finance.list_movements",
            new JsonObject { ["search"] = "hosting", ["pageSize"] = 1 }, CancellationToken.None);
        var output = result.Data["result"]!;

        Assert.Single(output["items"]!.AsArray());
        Assert.Equal(150m, output["totals"]![0]!["total"]!.GetValue<decimal>());
        Assert.NotEmpty(result.Data["warnings"]!.AsArray());
    }

    [Fact]
    public async Task CompareMonthsShowsChangeAndCategoryDifferences()
    {
        await using var db = CreateDatabase();
        var (actor, _) = SeedProject(db);
        var category = new FinancialCategory { Name = "Sueldos", MovementType = FinancialMovementType.Egreso };
        db.Add(category);
        db.FinancialMovements.AddRange(
            Movement(category, null, 200, "ARS", FinancialMovementStatus.Pagado, new DateOnly(2026, 9, 10), FinancialMovementType.Egreso, settled: new DateOnly(2026, 9, 10)),
            Movement(category, null, 300, "ARS", FinancialMovementStatus.Pagado, new DateOnly(2026, 10, 10), FinancialMovementType.Egreso, settled: new DateOnly(2026, 10, 10)));
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "finance.compare_months",
            new JsonObject { ["year"] = 2026, ["month"] = 10 }, CancellationToken.None);
        var output = result.Data["result"]!;

        Assert.Equal(9, output["comparedWith"]!["month"]!.GetValue<int>());
        var ars = output["differences"]!.AsArray().Single(x => x!["currency"]!.GetValue<string>() == "ARS")!;
        Assert.Equal(100m, ars["expense"]!["change"]!.GetValue<decimal>());
        Assert.Equal(50m, ars["expense"]!["changePercent"]!.GetValue<decimal>());
        Assert.Equal("Sueldos", output["expenseCategoryChanges"]![0]!["category"]!.GetValue<string>());
    }

    [Fact]
    public async Task MyDayJoinsTasksMeetingsAndFinanceForToday()
    {
        await using var db = CreateDatabase();
        var (actor, project) = SeedProject(db);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone));
        var category = new FinancialCategory { Name = "Servicios", MovementType = FinancialMovementType.Ingreso };
        db.Add(category);
        db.Tasks.AddRange(
            new TaskItem { Titulo = "Hoy", ProyectoId = project.Id, CreadoPorId = actor.UserId, FechaVencimiento = today },
            new TaskItem { Titulo = "Atrasada", ProyectoId = project.Id, CreadoPorId = actor.UserId, FechaVencimiento = today.AddDays(-3) },
            new TaskItem { Titulo = "Terminada", ProyectoId = project.Id, CreadoPorId = actor.UserId, FechaVencimiento = today, Estado = Kodvian.Core.Domain.Enums.TaskStatus.Finalizada });
        var noonUtc = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(new TimeOnly(12, 0)), AiSystemPromptBuilder.BuenosAiresTimeZone);
        db.Meetings.Add(new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Daily", Inicio = noonUtc, Fin = noonUtc.AddMinutes(30) });
        db.FinancialMovements.Add(Movement(category, null, 400, "ARS", FinancialMovementStatus.Pendiente, today));
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "my_day.get", new JsonObject(), CancellationToken.None);
        var output = result.Data["result"]!;

        Assert.Equal("Hoy", output["tasksDueToday"]!.AsArray().Single()!["title"]!.GetValue<string>());
        Assert.Equal(3, output["overdueTasks"]!.AsArray().Single()!["daysOverdue"]!.GetValue<int>());
        Assert.Equal("12:00", output["meetings"]!.AsArray().Single()!["starts"]!.GetValue<string>());
        Assert.Single(output["financeDueToday"]!.AsArray());
    }

    [Fact]
    public async Task ProjectAndClientDetailsAggregateWithoutPrivateClientData()
    {
        await using var db = CreateDatabase();
        var actor = AdminActor();
        var client = new Client { CommercialName = "Cliente Visible", TaxId = "30-11111111-1", ContactEmail = "privado@example.test", Notes = "Nota secreta" };
        var project = new Project { ClienteId = client.Id, Nombre = "Portal", PorcentajeAvance = 40, FechaEntregaEstimada = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10) };
        var category = new FinancialCategory { Name = "Servicios", MovementType = FinancialMovementType.Ingreso };
        db.AddRange(client, project, category, new User { Id = actor.UserId, FullName = "Admin", Email = "admin@example.test" });
        db.Tasks.Add(new TaskItem { Titulo = "Atrasada", ProyectoId = project.Id, CreadoPorId = actor.UserId, FechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-4) });
        var pending = Movement(category, client.Id, 800, "USD", FinancialMovementStatus.Pendiente, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        pending.ProjectId = project.Id;
        db.FinancialMovements.Add(pending);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var projectResult = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "projects.get", new JsonObject { ["projectId"] = project.Id.ToString() }, CancellationToken.None);
        var projectOutput = projectResult.Data["result"]!;
        Assert.Equal(40, projectOutput["project"]!["progress"]!.GetValue<int>());
        Assert.Single(projectOutput["overdueTasks"]!.AsArray());
        Assert.Equal(800m, projectOutput["finance"]![0]!["pending"]!.GetValue<decimal>());

        var clientResult = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "t", "clients.get", new JsonObject { ["clientId"] = client.Id.ToString() }, CancellationToken.None);
        var json = clientResult.Data.ToJsonString();
        Assert.Contains("Portal", json);
        Assert.DoesNotContain("30-11111111-1", json);
        Assert.DoesNotContain("privado@example.test", json);
        Assert.DoesNotContain("Nota secreta", json);
        Assert.Equal(800m, clientResult.Data["result"]!["receivables"]![0]!["total"]!.GetValue<decimal>());
    }

    private static FinancialMovement Movement(FinancialCategory category, Guid? clientId, decimal amount, string currency, FinancialMovementStatus status, DateOnly date,
        FinancialMovementType type = FinancialMovementType.Ingreso, string description = "Movimiento", DateOnly? settled = null) => new()
    {
        CategoryId = category.Id, ClientId = clientId, Amount = amount, Currency = currency, Status = status, MovementDate = date, DueDate = date,
        MovementType = type, Description = description, CreatedById = Guid.NewGuid(), SettlementDate = settled
    };

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
    public async Task ToolsRequireAiPermissionAndTheDomainPermissionsOfEachTool()
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

        // Un Analista ve lo operativo pero no finanzas, panel ejecutivo ni registro de movimientos.
        var analystTools = catalog.GetAvailableTools(RoleActor(RoleNames.Analyst)).Select(x => x.Name).ToArray();
        Assert.Contains("tasks.create", analystTools);
        Assert.Contains("meetings.create", analystTools);
        Assert.Contains("clients.create_prospect", analystTools);
        Assert.DoesNotContain("finance.get_summary", analystTools);
        Assert.DoesNotContain("finance.list_pending", analystTools);
        Assert.DoesNotContain("finance.create_movement", analystTools);
        Assert.DoesNotContain("dashboard.get_snapshot", analystTools);

        // Un Desarrollador solo ve lo suyo: su día, sus tareas, sus reuniones y el cambio de estado de sus tareas.
        var developerTools = catalog.GetAvailableTools(RoleActor(RoleNames.Developer)).Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal(["meetings.list", "my_day.get", "preferences.update", "tasks.list", "tasks.update_status"], developerTools);
    }

    [Fact]
    public async Task ReadOnlyUsersOnlyGetQueriesAndCannotConfirmActions()
    {
        await using var db = CreateDatabase();
        var (admin, project) = SeedProject(db);
        var task = new TaskItem { Titulo = "Tarea", ProyectoId = project.Id, CreadoPorId = admin.UserId };
        db.Tasks.Add(task);
        db.SaveChanges();
        var catalog = CreateCatalog(db);
        var readOnly = RoleActor(RoleNames.ReadOnly);

        var tools = catalog.GetAvailableTools(readOnly);
        // Solo consultas; la única acción es guardar sus propias preferencias.
        Assert.Equal(["preferences.update"], tools.Where(x => x.RequiresConfirmation).Select(x => x.Name).ToArray());
        Assert.Contains(tools, x => x.Name == "projects.list");
        Assert.Contains(tools, x => x.Name == "tasks.list");
        Assert.DoesNotContain(tools, x => x.Name.StartsWith("finance.", StringComparison.Ordinal) || x.Name == "dashboard.get_snapshot");

        await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(readOnly, Guid.NewGuid(), Guid.Empty, "t", "tasks.update_status",
            new JsonObject { ["taskId"] = task.Id.ToString(), ["status"] = "Finalizada" }, CancellationToken.None));
        await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteConfirmedActionAsync(readOnly, Guid.NewGuid(), "tasks.update_status",
            new JsonObject { ["taskId"] = task.Id.ToString(), ["status"] = "Finalizada" }, CancellationToken.None));
        await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(readOnly, Guid.NewGuid(), Guid.Empty, "t", "finance.list_pending", new JsonObject(), CancellationToken.None));
        Assert.Equal(Kodvian.Core.Domain.Enums.TaskStatus.Pendiente, db.Tasks.AsNoTracking().Single().Estado);
    }

    [Fact]
    public async Task UserWithoutPermissionCannotExecuteOrConfirmTheTool()
    {
        await using var db = CreateDatabase();
        var (_, project) = SeedProject(db);
        var catalog = CreateCatalog(db);
        var developer = RoleActor(RoleNames.Developer);

        await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(developer, Guid.NewGuid(), Guid.Empty, "t", "finance.get_summary", new JsonObject(), CancellationToken.None));
        await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteConfirmedActionAsync(developer, Guid.NewGuid(), "tasks.create",
            new JsonObject { ["projectId"] = project.Id.ToString(), ["title"] = "X" }, CancellationToken.None));
        Assert.Empty(db.Tasks);
        Assert.Contains(db.AiAuditEvents, x => x.Decision == "Denied" && x.ReasonCode == "permission_denied");
    }

    [Fact]
    public async Task DeveloperOnlySeesAndChangesTheirOwnTasks()
    {
        await using var db = CreateDatabase();
        var (admin, project) = SeedProject(db);
        var developerProfile = new Developer { FullName = "Dev Uno" };
        var developer = RoleActor(RoleNames.Developer);
        db.Developers.Add(developerProfile);
        db.Users.Add(new User { Id = developer.UserId, FullName = "Dev Uno", Email = "dev@example.test", DeveloperId = developerProfile.Id });
        var mine = new TaskItem { Titulo = "Mi tarea", ProyectoId = project.Id, CreadoPorId = admin.UserId, DeveloperId = developerProfile.Id };
        var other = new TaskItem { Titulo = "Tarea ajena", ProyectoId = project.Id, CreadoPorId = admin.UserId };
        db.Tasks.AddRange(mine, other);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var list = await catalog.ExecuteAsync(developer, Guid.NewGuid(), Guid.Empty, "t", "tasks.list", new JsonObject(), CancellationToken.None);
        var json = list.Data.ToJsonString();
        Assert.Contains("Mi tarea", json);
        Assert.DoesNotContain("Tarea ajena", json);

        var proposal = await catalog.ExecuteAsync(developer, Guid.NewGuid(), Guid.Empty, "t", "tasks.update_status",
            new JsonObject { ["taskId"] = mine.Id.ToString(), ["status"] = "EnCurso" }, CancellationToken.None);
        await catalog.ExecuteConfirmedActionAsync(developer, Guid.NewGuid(), "tasks.update_status", proposal.Proposal!.Arguments, CancellationToken.None);
        Assert.Equal(Kodvian.Core.Domain.Enums.TaskStatus.EnCurso, db.Tasks.AsNoTracking().Single(x => x.Id == mine.Id).Estado);

        var error = await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(developer, Guid.NewGuid(), Guid.Empty, "t", "tasks.update_status",
            new JsonObject { ["taskId"] = other.Id.ToString(), ["status"] = "EnCurso" }, CancellationToken.None));
        Assert.Contains("tus propias tareas", error.Message);
        Assert.Equal(Kodvian.Core.Domain.Enums.TaskStatus.Pendiente, db.Tasks.AsNoTracking().Single(x => x.Id == other.Id).Estado);
    }

    [Fact]
    public async Task NonAdministratorsOnlySeeMeetingsTheyTakePartIn()
    {
        await using var db = CreateDatabase();
        var (admin, project) = SeedProject(db);
        var operative = RoleActor(RoleNames.Operative);
        db.Users.Add(new User { Id = operative.UserId, FullName = "Operativo", Email = "op@example.test" });
        var start = DateTime.UtcNow.AddDays(1);
        var mine = new Meeting { ProyectoId = project.Id, CreadoPorId = operative.UserId, Titulo = "Reunion mia", Inicio = start, Fin = start.AddHours(1) };
        var other = new Meeting { ProyectoId = project.Id, CreadoPorId = admin.UserId, Titulo = "Reunion de direccion", Inicio = start, Fin = start.AddHours(1) };
        db.Meetings.AddRange(mine, other);
        db.SaveChanges();
        var catalog = CreateCatalog(db);

        var json = (await catalog.ExecuteAsync(operative, Guid.NewGuid(), Guid.Empty, "t", "meetings.list", new JsonObject(), CancellationToken.None)).Data.ToJsonString();
        Assert.Contains("Reunion mia", json);
        Assert.DoesNotContain("Reunion de direccion", json);

        var error = await Assert.ThrowsAsync<AiToolRejectedException>(() => catalog.ExecuteAsync(operative, Guid.NewGuid(), Guid.Empty, "t", "meetings.cancel",
            new JsonObject { ["meetingId"] = other.Id.ToString() }, CancellationToken.None));
        Assert.Contains("No tenés acceso", error.Message);
    }

    private static AiExecutionContext RoleActor(string role) => new()
    {
        UserId = Guid.NewGuid(),
        SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([role], StringComparer.Ordinal),
        Permissions = new HashSet<string>(RolePermissionMap.GetPermissions(role), StringComparer.Ordinal)
    };

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
            new FinancialMovementService(db, new NullFileStorage(), Options.Create(new StorageOptions()), finance),
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
