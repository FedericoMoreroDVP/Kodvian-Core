namespace Kodvian.Core.Domain.Entities;

public class DeveloperPayment : BaseEntity
{
    public Guid ContractId { get; set; }
    public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? AppliedCurrency { get; set; }
    public decimal? AppliedAmount { get; set; }
    public Guid? FinancialMovementId { get; set; }
    public FinancialMovement? FinancialMovement { get; set; }
    public Guid? RequestId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public ProjectDeveloperContract? Contract { get; set; }
    public ICollection<DocumentFile> Documents { get; set; } = new List<DocumentFile>();
}
