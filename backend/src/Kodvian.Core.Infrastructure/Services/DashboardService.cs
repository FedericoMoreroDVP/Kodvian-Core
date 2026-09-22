using Kodvian.Core.Application.Dashboard.Abstractions;
using Kodvian.Core.Application.Dashboard.Dtos;
using Kodvian.Core.Application.Dashboard.Requests;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainTaskStatus = Kodvian.Core.Domain.Enums.TaskStatus;

namespace Kodvian.Core.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IFinanceOverviewService _finance;

    public DashboardService(KodvianDbContext dbContext, IFinanceOverviewService finance)
    {
        _dbContext = dbContext;
        _finance = finance;
    }

    public async Task<DashboardOverviewDto> GetOverviewAsync(DashboardOverviewRequestDto request, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(utcNow);
        var year = request.Year ?? utcNow.Year;
        var month = request.Month ?? utcNow.Month;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var activeClients = await _dbContext.Clients
            .AsNoTracking()
            .CountAsync(x => x.Activo, cancellationToken);

        var projectsInProgress = await _dbContext.Projects
            .AsNoTracking()
            .CountAsync(x => x.Activo && x.Estado == ProjectStatus.EnCurso, cancellationToken);

        var overdueTasks = await _dbContext.Tasks
            .AsNoTracking()
            .CountAsync(x => x.Activo
                && x.FechaVencimiento.HasValue
                && x.FechaVencimiento.Value < today
                && x.Estado != DomainTaskStatus.Finalizada
                && x.Estado != DomainTaskStatus.Cancelada,
                cancellationToken);

        var tasksForToday = await _dbContext.Tasks
            .AsNoTracking()
            .CountAsync(x => x.Activo && x.FechaVencimiento == today, cancellationToken);

        var finance = await _finance.GetPeriodSummaryAsync(monthStart, monthEnd, cancellationToken);

        var priorityTasks = await _dbContext.Tasks
            .AsNoTracking()
            .Where(x => x.Activo && x.Estado != DomainTaskStatus.Finalizada && x.Estado != DomainTaskStatus.Cancelada)
            .OrderByDescending(x => x.Prioridad)
            .ThenBy(x => x.FechaVencimiento)
            .ThenBy(x => x.OrdenKanban)
            .Take(8)
            .Select(x => new DashboardPriorityTaskDto
            {
                Id = x.Id,
                Title = x.Titulo,
                ProjectName = x.Proyecto != null ? x.Proyecto.Nombre : string.Empty,
                ResponsibleName = x.Responsable != null ? x.Responsable.FullName : null,
                Status = x.Estado.ToString(),
                Priority = x.Prioridad.ToString(),
                DueDate = x.FechaVencimiento
            })
            .ToListAsync(cancellationToken);

        var collectionsWindowEnd = today.AddDays(7);
        var upcomingCollections = await _dbContext.FinancialMovements
            .AsNoTracking()
            .Where(x => x.MovementType == FinancialMovementType.Ingreso
                        && x.Status == FinancialMovementStatus.Pendiente
                        && x.DueDate.HasValue
                        && x.DueDate.Value >= today
                        && x.DueDate.Value <= collectionsWindowEnd)
            .OrderBy(x => x.DueDate)
            .Take(8)
            .Select(x => new DashboardUpcomingCollectionDto
            {
                Id = x.Id,
                Description = x.Description,
                CategoryName = x.Category != null ? x.Category.Name : string.Empty,
                ClientName = x.Client != null ? x.Client.CommercialName : null,
                Amount = x.Amount,
                Currency = x.Currency,
                DueDate = x.DueDate,
                Status = x.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        var recentMovements = await _dbContext.FinancialMovements
            .AsNoTracking()
            .OrderByDescending(x => x.MovementDate)
            .ThenByDescending(x => x.FechaCreacion)
            .Take(10)
            .Select(x => new DashboardRecentMovementDto
            {
                Id = x.Id,
                MovementType = x.MovementType.ToString(),
                Description = x.Description,
                CategoryName = x.Category != null ? x.Category.Name : string.Empty,
                Amount = x.Amount,
                Currency = x.Currency,
                MovementDate = x.MovementDate,
                Status = x.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        var ars = finance.Currencies.Single(x => x.Currency == "ARS");

        return new DashboardOverviewDto
        {
            Finance = finance,
            Kpis = new DashboardKpisDto
            {
                ActiveClients = activeClients,
                ProjectsInProgress = projectsInProgress,
                OverdueTasks = overdueTasks,
                TasksForToday = tasksForToday,
                MonthlyIncome = ars.Income,
                MonthlyExpense = ars.Expense,
                MonthlyResult = ars.Result,
                PendingCollections = ars.PendingIncome,
                PendingPayments = ars.PendingExpense
            },
            PriorityTasks = priorityTasks.AsReadOnly(),
            UpcomingCollections = upcomingCollections.AsReadOnly(),
            RecentMovements = recentMovements.AsReadOnly()
        };
    }
}
