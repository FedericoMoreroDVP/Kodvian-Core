namespace Kodvian.Core.Domain.Entities;

public class FinancialIncomePartnerDistribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllocationId { get; set; }
    public FinancialIncomeAllocation Allocation { get; set; } = null!;
    public Guid PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;
    public string PartnerName { get; set; } = string.Empty;
    public decimal Percentage { get; set; }
    public decimal Amount { get; set; }
}
