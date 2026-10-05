namespace Kodvian.Core.Application.Projects.Requests;

public class ProjectUpsertRequestDto
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CalendarColor { get; set; }
    public Guid? ResponsibleId { get; set; }
    public string Status { get; set; } = "Planificacion";
    public string Priority { get; set; } = "Media";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EstimatedDeliveryDate { get; set; }
    public DateOnly? ClosingDate { get; set; }
    public decimal? Budget { get; set; }
    public int ProgressPercentage { get; set; }
    public bool IsActive { get; set; } = true;
}
