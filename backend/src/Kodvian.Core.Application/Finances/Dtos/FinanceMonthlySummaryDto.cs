namespace Kodvian.Core.Application.Finances.Dtos;

public class FinanceMonthlySummaryDto
{
    public IReadOnlyCollection<Kodvian.Core.Application.Finances.Abstractions.CurrencyPeriodDto> Currencies { get; set; } = [];
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpense { get; set; }
    public decimal MonthlyResult { get; set; }
    public decimal PendingIncome { get; set; }
    public decimal PendingExpense { get; set; }
}
