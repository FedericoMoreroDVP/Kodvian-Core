namespace Kodvian.Core.Domain.Entities;

public class FinancialIncomeAllocation : BaseEntity
{
    public Guid IncomeMovementId { get; set; }
    public FinancialMovement IncomeMovement { get; set; } = null!;
    public string Currency { get; set; } = "ARS";
    public decimal GrossAmount { get; set; }
    public decimal ExpensesCovered { get; set; }
    public decimal CapitalizedAmount { get; set; }
    public decimal DistributableAmount { get; set; }
    public bool SharesConfigured { get; set; }
    public ICollection<FinancialIncomeExpenseApplication> ExpenseApplications { get; set; } = new List<FinancialIncomeExpenseApplication>();
    public ICollection<FinancialIncomePartnerDistribution> PartnerDistributions { get; set; } = new List<FinancialIncomePartnerDistribution>();
}
