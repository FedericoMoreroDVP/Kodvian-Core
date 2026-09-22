using Kodvian.Core.Domain.Enums;

namespace Kodvian.Core.Domain.Entities;

public class ProjectDeveloperContract : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid DeveloperId { get; set; }
    public ContractPaymentMode PaymentMode { get; set; } = ContractPaymentMode.Percentage;
    public decimal? Percentage { get; set; }
    public decimal? AgreedAmount { get; set; }
    public string? Currency { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; }

    public Project? Project { get; set; }
    public Developer? Developer { get; set; }
    public ICollection<DeveloperPayment> Payments { get; set; } = new List<DeveloperPayment>();
}
