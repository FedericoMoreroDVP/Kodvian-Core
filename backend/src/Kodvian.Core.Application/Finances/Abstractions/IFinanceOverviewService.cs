namespace Kodvian.Core.Application.Finances.Abstractions;

public class FinanceSetupDto
{
    public DateOnly? StartDate { get; set; }
    public decimal? OpeningArs { get; set; }
    public decimal? OpeningUsd { get; set; }
    public bool HistoryComplete { get; set; }
    public Guid Version { get; set; }
}
public record PartnerDto(Guid Id, string FullName, bool IsActive);
public class PartnerRequest { public string FullName { get; set; } = ""; public bool IsActive { get; set; } = true; }
public class CurrencyOverviewDto
{
    public string Currency { get; set; } = "ARS";
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Result => Income - Expense;
    public decimal Contributions { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal Reimbursements { get; set; }
    public decimal PendingIncome { get; set; }
    public decimal PendingExpense { get; set; }
    public decimal CashChange { get; set; }
    public decimal? Balance { get; set; }
}
public class FinanceMonthDto : CurrencyOverviewDto { public int Year { get; set; } public int Month { get; set; } }
public record ExpenseCategoryDto(string Currency, string Category, decimal Amount);
public record PartnerBalanceDto(Guid PartnerId, string PartnerName, string Currency, decimal Contributions, decimal Withdrawals, decimal ReimbursableExpenses, decimal Reimbursements)
{
    public decimal Outstanding => ReimbursableExpenses - Reimbursements;
}
public class FinanceOverviewDto
{
    public FinanceSetupDto Setup { get; set; } = new();
    public DateOnly? From { get; set; }
    public DateOnly To { get; set; }
    public int EstimatedDateCount { get; set; }
    public int UnclassifiedPayments { get; set; }
    public IReadOnlyCollection<CurrencyOverviewDto> Currencies { get; set; } = [];
    public IReadOnlyCollection<FinanceMonthDto> Months { get; set; } = [];
    public IReadOnlyCollection<ExpenseCategoryDto> Expenses { get; set; } = [];
    public IReadOnlyCollection<PartnerBalanceDto> Partners { get; set; } = [];
}
public class ExchangeRequest
{
    public Guid RequestId { get; set; }
    public string FromCurrency { get; set; } = "ARS";
    public decimal FromAmount { get; set; }
    public string ToCurrency { get; set; } = "USD";
    public decimal ToAmount { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
}
public interface IFinanceOverviewService
{
    Task<Kodvian.Core.Application.Common.Models.PagedResultDto<Kodvian.Core.Application.Developers.Dtos.ContractLedgerDto>> TeamObligationsAsync(int year, Kodvian.Core.Application.Common.Models.PagedRequestDto request, CancellationToken ct);
    Task<FinanceOverviewDto> GetAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<FinanceSetupDto> SetupAsync(FinanceSetupDto request, CancellationToken ct);
    Task<IReadOnlyCollection<PartnerDto>> PartnersAsync(CancellationToken ct);
    Task<PartnerDto> SavePartnerAsync(Guid? id, PartnerRequest request, CancellationToken ct);
    Task<Guid> ExchangeAsync(ExchangeRequest request, CancellationToken ct);
    Task CancelExchangeAsync(Guid id, CancellationToken ct);
}
