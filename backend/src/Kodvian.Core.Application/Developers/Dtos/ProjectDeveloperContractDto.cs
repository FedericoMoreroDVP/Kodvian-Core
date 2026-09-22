namespace Kodvian.Core.Application.Developers.Dtos;

public class ProjectDeveloperContractDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public Guid DeveloperId { get; set; }
    public string DeveloperName { get; set; } = string.Empty;
    public string PaymentMode { get; set; } = string.Empty;
    public decimal? Percentage { get; set; }
    public decimal? AgreedAmount { get; set; }
    public string? Currency { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}
