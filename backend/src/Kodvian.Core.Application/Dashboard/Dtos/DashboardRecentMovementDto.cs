namespace Kodvian.Core.Application.Dashboard.Dtos;

public class DashboardRecentMovementDto
{
    public Guid Id { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateOnly MovementDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
