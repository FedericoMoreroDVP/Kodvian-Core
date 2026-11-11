namespace Kodvian.Core.Domain.Entities;

public class FinancialIncomeExpenseApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllocationId { get; set; }
    public FinancialIncomeAllocation Allocation { get; set; } = null!;
    public Guid ExpenseMovementId { get; set; }
    public FinancialMovement ExpenseMovement { get; set; } = null!;
    public decimal Amount { get; set; }
}
