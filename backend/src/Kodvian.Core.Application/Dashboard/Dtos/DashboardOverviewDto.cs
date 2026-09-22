namespace Kodvian.Core.Application.Dashboard.Dtos;

public class DashboardOverviewDto
{
    public Kodvian.Core.Application.Finances.Abstractions.FinancePeriodSummaryDto Finance { get; set; } = new();
    public DashboardKpisDto Kpis { get; set; } = new();
    public IReadOnlyCollection<DashboardPriorityTaskDto> PriorityTasks { get; set; } = Array.Empty<DashboardPriorityTaskDto>();
    public IReadOnlyCollection<DashboardUpcomingCollectionDto> UpcomingCollections { get; set; } = Array.Empty<DashboardUpcomingCollectionDto>();
    public IReadOnlyCollection<DashboardRecentMovementDto> RecentMovements { get; set; } = Array.Empty<DashboardRecentMovementDto>();
}
