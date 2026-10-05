namespace Kodvian.Core.Application.Projects.Dtos;

public class ProjectListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public Guid? ResponsibleId { get; set; }
    public string? ResponsibleName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string CalendarColor { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EstimatedDeliveryDate { get; set; }
    public int ProgressPercentage { get; set; }
    public bool IsActive { get; set; }
}
