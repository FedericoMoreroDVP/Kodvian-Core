namespace Kodvian.Core.Application.Developers.Requests;

public class ProjectDeveloperContractUpsertRequestDto
{
    public Guid DeveloperId { get; set; }
    public string PaymentMode { get; set; } = "Percentage";
    public decimal? Percentage { get; set; }
    public decimal? AgreedAmount { get; set; }
    public string? Currency { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
