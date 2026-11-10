using System.Globalization;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainTaskStatus = Kodvian.Core.Domain.Enums.TaskStatus;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>
/// Resumen proactivo de Oficina Inteligente: tareas vencidas, reuniones de la semana, cobros pendientes,
/// proyectos en riesgo y alertas con una consulta sugerida para el asistente. Consultas directas, sin IA.
/// </summary>
public sealed class AiBriefingService(KodvianDbContext dbContext, ISessionValidator sessionValidator) : IAiBriefingService
{
    internal const int OverdueTaskAlertDays = 7;
    internal const int ProjectRiskWindowDays = 14;
    internal const int ProjectRiskMaxProgress = 70;
    internal const decimal ExpenseSpikeRatio = 0.30m;
    private static readonly TimeZoneInfo BuenosAires = AiSystemPromptBuilder.BuenosAiresTimeZone;

    public async Task<AiBriefingDto> GetAsync(AiExecutionContext actor, CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(PermissionCodes.AiOfficeUse))
            throw new UnauthorizedAccessException("No tienes acceso a Oficina Inteligente.");
        if (!await sessionValidator.IsValidAsync(actor.UserId, actor.SessionVersion, cancellationToken))
            throw new UnauthorizedAccessException("La sesión dejó de estar vigente.");

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BuenosAires);
        var today = DateOnly.FromDateTime(nowLocal);
        var weekEnd = today.AddDays(6);
        var isAdministrator = actor.Roles.Contains(RoleNames.Administrator);
        var user = await dbContext.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => new { x.FullName, x.DeveloperId }).SingleOrDefaultAsync(cancellationToken);
        var alerts = new List<AiAlertDto>();

        AiBriefingTasksDto? tasksSection = null;
        if (actor.Permissions.Contains(PermissionCodes.TasksRead) || actor.Permissions.Contains(PermissionCodes.DeveloperWorkRead))
        {
            var query = dbContext.Tasks.AsNoTracking().Where(x => x.Activo && x.FechaVencimiento.HasValue
                && x.Estado != DomainTaskStatus.Finalizada && x.Estado != DomainTaskStatus.Cancelada && x.FechaVencimiento <= weekEnd);
            // Fuera de Administración cada uno ve solo lo propio.
            if (!isAdministrator)
                query = query.Where(x => x.ResponsableId == actor.UserId || (user != null && user.DeveloperId.HasValue && x.DeveloperId == user.DeveloperId));
            var rows = await query.OrderBy(x => x.FechaVencimiento).Take(300)
                .Select(x => new { x.Id, x.Titulo, x.FechaVencimiento, Project = x.Proyecto != null ? x.Proyecto.Nombre : null }).ToListAsync(cancellationToken);
            var overdue = rows.Where(x => x.FechaVencimiento < today).ToList();
            tasksSection = new AiBriefingTasksDto
            {
                Overdue = overdue.Count,
                DueToday = rows.Count(x => x.FechaVencimiento == today),
                DueThisWeek = rows.Count(x => x.FechaVencimiento >= today),
                TopOverdue = overdue.Take(5).Select(x => new AiBriefingItemDto
                {
                    Id = x.Id, Title = x.Titulo,
                    Detail = $"{(x.Project is null ? string.Empty : x.Project + " · ")}vencida hace {today.DayNumber - x.FechaVencimiento!.Value.DayNumber} días"
                }).ToArray()
            };
            var stale = overdue.Count(x => today.DayNumber - x.FechaVencimiento!.Value.DayNumber > OverdueTaskAlertDays);
            if (stale > 0)
                alerts.Add(new AiAlertDto
                {
                    Kind = "overdue_tasks",
                    Severity = stale >= 10 ? "critical" : "warning",
                    Title = stale == 1 ? "1 tarea vencida hace más de una semana" : $"{stale} tareas vencidas hace más de una semana",
                    Detail = "Conviene reprogramarlas, reasignarlas o cerrarlas para que el tablero refleje la realidad.",
                    Prompt = "Mostrame las tareas vencidas hace más de 7 días y ayudame a reprogramarlas",
                    Link = "/tareas"
                });
        }

        AiBriefingMeetingsDto? meetingsSection = null;
        if (actor.Permissions.Contains(PermissionCodes.MeetingsRead))
        {
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), BuenosAires);
            var endTodayUtc = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MaxValue), BuenosAires);
            var endWeekUtc = TimeZoneInfo.ConvertTimeToUtc(weekEnd.ToDateTime(TimeOnly.MaxValue), BuenosAires);
            var query = dbContext.Meetings.AsNoTracking().Where(x => x.Activo && x.Estado != MeetingStatus.Cancelada && x.Fin >= startUtc && x.Inicio <= endWeekUtc);
            if (!isAdministrator) query = query.Where(x => x.CreadoPorId == actor.UserId || x.Participantes.Any(p => p.UserId == actor.UserId));
            var rows = await query.OrderBy(x => x.Inicio).Take(100)
                .Select(x => new { x.Id, x.Titulo, x.Inicio, Project = x.Proyecto != null ? x.Proyecto.Nombre : null }).ToListAsync(cancellationToken);
            var nowUtc = DateTime.UtcNow;
            meetingsSection = new AiBriefingMeetingsDto
            {
                Today = rows.Count(x => x.Inicio <= endTodayUtc),
                ThisWeek = rows.Count,
                Next = rows.Where(x => x.Inicio >= nowUtc).Take(4).Select(x => new AiBriefingItemDto
                {
                    Id = x.Id, Title = x.Titulo,
                    Detail = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.Inicio, DateTimeKind.Utc), BuenosAires).ToString("ddd d/M HH:mm", CultureInfo.GetCultureInfo("es-AR"))
                        + (x.Project is null ? string.Empty : " · " + x.Project)
                }).ToArray()
            };
        }

        IReadOnlyCollection<AiBriefingMoneyDto>? collections = null;
        if (actor.Permissions.Contains(PermissionCodes.FinancesRead))
        {
            var pending = await dbContext.FinancialMovements.AsNoTracking()
                .Where(x => x.Activo && x.MovementType == FinancialMovementType.Ingreso
                    && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido))
                .Select(x => new { x.Currency, x.Amount, Due = x.DueDate ?? x.MovementDate }).ToListAsync(cancellationToken);
            collections = pending.GroupBy(x => x.Currency).OrderBy(g => g.Key).Select(g => new AiBriefingMoneyDto
            {
                Currency = g.Key, Pending = g.Sum(x => x.Amount), Overdue = g.Where(x => x.Due < today).Sum(x => x.Amount), Count = g.Count()
            }).ToArray();
            var overdueCollections = collections.Where(x => x.Overdue > 0).ToList();
            if (overdueCollections.Count > 0)
                alerts.Add(new AiAlertDto
                {
                    Kind = "overdue_collections",
                    Severity = "warning",
                    Title = "Hay cobros vencidos",
                    Detail = string.Join(" · ", overdueCollections.Select(x => $"{x.Currency} {x.Overdue.ToString("N0", CultureInfo.GetCultureInfo("es-AR"))}")) + " sin cobrar después del vencimiento.",
                    Prompt = "¿Quién me debe? Ordenalo por días de atraso",
                    Link = "/finanzas"
                });

            alerts.AddRange(await ExpenseSpikeAlertsAsync(today, cancellationToken));
        }

        IReadOnlyCollection<AiBriefingProjectDto>? projectsAtRisk = null;
        if (actor.Permissions.Contains(PermissionCodes.ProjectsRead) && isAdministrator)
        {
            var limit = today.AddDays(ProjectRiskWindowDays);
            var rows = await dbContext.Projects.AsNoTracking()
                .Where(x => x.Activo && (x.Estado == ProjectStatus.EnCurso || x.Estado == ProjectStatus.Planificacion)
                    && x.FechaEntregaEstimada.HasValue && x.FechaEntregaEstimada <= limit && x.PorcentajeAvance < ProjectRiskMaxProgress)
                .OrderBy(x => x.FechaEntregaEstimada).Take(5)
                .Select(x => new { x.Id, x.Nombre, x.PorcentajeAvance, x.FechaEntregaEstimada }).ToListAsync(cancellationToken);
            projectsAtRisk = rows.Select(x => new AiBriefingProjectDto
            {
                Id = x.Id, Name = x.Nombre, Progress = x.PorcentajeAvance, EstimatedDelivery = x.FechaEntregaEstimada,
                DaysToDelivery = x.FechaEntregaEstimada!.Value.DayNumber - today.DayNumber
            }).ToArray();
            foreach (var project in projectsAtRisk.Take(3))
                alerts.Add(new AiAlertDto
                {
                    Kind = "project_risk",
                    Severity = project.DaysToDelivery < 0 ? "critical" : "warning",
                    Title = project.DaysToDelivery < 0
                        ? $"{project.Name}: la entrega venció hace {-project.DaysToDelivery} días"
                        : $"{project.Name}: entrega en {project.DaysToDelivery} días con {project.Progress}% de avance",
                    Detail = "Revisá las tareas pendientes y si hace falta mover la fecha o sumar gente.",
                    Prompt = $"¿Cómo viene el proyecto {project.Name}? Mostrame qué falta para la entrega",
                    Link = "/proyectos"
                });
        }

        var hour = nowLocal.Hour;
        var firstName = user?.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var salutation = hour < 12 ? "Buen día" : hour < 20 ? "Buenas tardes" : "Buenas noches";
        return new AiBriefingDto
        {
            Date = today,
            Greeting = firstName is null ? salutation : $"{salutation}, {firstName}",
            Tasks = tasksSection,
            Meetings = meetingsSection,
            Collections = collections,
            ProjectsAtRisk = projectsAtRisk,
            Alerts = alerts.OrderBy(x => x.Severity == "critical" ? 0 : x.Severity == "warning" ? 1 : 2).ToArray()
        };
    }

    // Gastos operativos pagados en los últimos 30 días contra los 30 anteriores, por moneda.
    private async Task<IEnumerable<AiAlertDto>> ExpenseSpikeAlertsAsync(DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-59);
        var middle = today.AddDays(-29);
        var rows = await dbContext.FinancialMovements.AsNoTracking()
            .Where(x => x.Activo && x.Nature == "Operacion" && x.MovementType == FinancialMovementType.Egreso && x.Status == FinancialMovementStatus.Pagado
                && x.SettlementDate >= from && x.SettlementDate <= today)
            .Select(x => new { x.Currency, x.Amount, Date = x.SettlementDate!.Value }).ToListAsync(ct);
        var alerts = new List<AiAlertDto>();
        foreach (var currency in rows.Select(x => x.Currency).Distinct().OrderBy(x => x))
        {
            var recent = rows.Where(x => x.Currency == currency && x.Date >= middle).Sum(x => x.Amount);
            var previous = rows.Where(x => x.Currency == currency && x.Date < middle).Sum(x => x.Amount);
            if (previous <= 0 || recent <= previous * (1 + ExpenseSpikeRatio)) continue;
            var percent = Math.Round((recent - previous) / previous * 100);
            alerts.Add(new AiAlertDto
            {
                Kind = "expense_spike",
                Severity = percent >= 60 ? "critical" : "warning",
                Title = $"Los gastos en {currency} subieron {percent}%",
                Detail = $"Últimos 30 días: {currency} {recent.ToString("N0", CultureInfo.GetCultureInfo("es-AR"))} contra {previous.ToString("N0", CultureInfo.GetCultureInfo("es-AR"))} en los 30 anteriores.",
                Prompt = $"¿Por qué subieron los gastos en {currency}? Mostrame qué categorías crecieron",
                Link = "/vision-financiera"
            });
        }
        return alerts;
    }
}
