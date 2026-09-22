namespace Kodvian.Core.Application.Dashboard.Dtos;

public class DashboardUpcomingCollectionDto
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
