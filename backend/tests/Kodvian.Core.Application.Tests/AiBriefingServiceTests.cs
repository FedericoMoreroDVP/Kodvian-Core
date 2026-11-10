using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Application.Tests;

public sealed class AiBriefingServiceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone));

    [Fact]
    public async Task BriefingSummarizesTheDayAndRaisesAlerts()
    {
        await using var db = CreateDatabase();
        var actor = Admin();
        var client = new Client { CommercialName = "Acme" };
        var risky = new Project { ClienteId = client.Id, Nombre = "Portal", PorcentajeAvance = 30, Estado = ProjectStatus.EnCurso, FechaEntregaEstimada = Today.AddDays(5) };
        var healthy = new Project { ClienteId = client.Id, Nombre = "App", PorcentajeAvance = 90, Estado = ProjectStatus.EnCurso, FechaEntregaEstimada = Today.AddDays(5) };
        var income = new FinancialCategory { Name = "Servicios", MovementType = FinancialMovementType.Ingreso };
        var expense = new FinancialCategory { Name = "Hosting", MovementType = FinancialMovementType.Egreso };
        db.AddRange(client, risky, healthy, income, expense, new User { Id = actor.UserId, FullName = "Nicolás Prueba", Email = "n@example.test" });
        db.Tasks.AddRange(
            new TaskItem { Titulo = "Muy vencida", ProyectoId = risky.Id, CreadoPorId = actor.UserId, FechaVencimiento = Today.AddDays(-10) },
            new TaskItem { Titulo = "Vencida", ProyectoId = risky.Id, CreadoPorId = actor.UserId, FechaVencimiento = Today.AddDays(-2) },
            new TaskItem { Titulo = "Hoy", ProyectoId = risky.Id, CreadoPorId = actor.UserId, FechaVencimiento = Today });
        db.FinancialMovements.AddRange(
            Money(income, FinancialMovementType.Ingreso, 1000, FinancialMovementStatus.Pendiente, Today.AddDays(-3)),
            Money(expense, FinancialMovementType.Egreso, 100, FinancialMovementStatus.Pagado, Today.AddDays(-45), settled: Today.AddDays(-45)),
            Money(expense, FinancialMovementType.Egreso, 200, FinancialMovementStatus.Pagado, Today.AddDays(-5), settled: Today.AddDays(-5)));
        db.SaveChanges();

        var briefing = await new AiBriefingService(db, new ActiveSession()).GetAsync(actor, CancellationToken.None);

        Assert.StartsWith("Bu", briefing.Greeting);
        Assert.EndsWith("Nicolás", briefing.Greeting);
        Assert.Equal(2, briefing.Tasks!.Overdue);
        Assert.Equal(1, briefing.Tasks.DueToday);
        Assert.Equal("Portal", Assert.Single(briefing.ProjectsAtRisk!).Name);
        Assert.Equal(1000m, briefing.Collections!.Single().Overdue);
        Assert.Contains(briefing.Alerts, x => x.Kind == "overdue_tasks" && x.Title.StartsWith("1 tarea"));
        Assert.Contains(briefing.Alerts, x => x.Kind == "project_risk" && x.Prompt.Contains("Portal"));
        Assert.Contains(briefing.Alerts, x => x.Kind == "overdue_collections");
        Assert.Contains(briefing.Alerts, x => x.Kind == "expense_spike" && x.Title.Contains("100%"));
    }

    [Fact]
    public async Task SectionsFollowThePermissionsOfTheUser()
    {
        await using var db = CreateDatabase();
        var actor = new AiExecutionContext
        {
            UserId = Guid.NewGuid(), SessionVersion = Guid.NewGuid(),
            Roles = new HashSet<string>([RoleNames.Administrator]),
            Permissions = new HashSet<string>([PermissionCodes.AiOfficeUse, PermissionCodes.TasksRead])
        };

        var briefing = await new AiBriefingService(db, new ActiveSession()).GetAsync(actor, CancellationToken.None);

        Assert.NotNull(briefing.Tasks);
        Assert.Null(briefing.Collections);
        Assert.Null(briefing.Meetings);
        Assert.Null(briefing.ProjectsAtRisk);
    }

    private static FinancialMovement Money(FinancialCategory category, FinancialMovementType type, decimal amount, FinancialMovementStatus status, DateOnly date, DateOnly? settled = null) => new()
    {
        CategoryId = category.Id, MovementType = type, Amount = amount, Currency = "ARS", Status = status, MovementDate = date, DueDate = date,
        SettlementDate = settled, Description = "Mov", CreatedById = Guid.NewGuid()
    };

    private static AiExecutionContext Admin() => new()
    {
        UserId = Guid.NewGuid(), SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([RoleNames.Administrator]),
        Permissions = new HashSet<string>(RolePermissionMap.GetPermissions(RoleNames.Administrator))
    };

    private static KodvianDbContext CreateDatabase()
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class ActiveSession : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }
}
