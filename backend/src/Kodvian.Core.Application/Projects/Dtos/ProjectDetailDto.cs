namespace Kodvian.Core.Application.Projects.Dtos;

public class ProjectDetailDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ResponsibleId { get; set; }
    public string? ResponsibleName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string CalendarColor { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EstimatedDeliveryDate { get; set; }
    public DateOnly? ClosingDate { get; set; }
    public decimal? Budget { get; set; }
    public int ProgressPercentage { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
