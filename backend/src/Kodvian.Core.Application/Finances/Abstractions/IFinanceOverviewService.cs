namespace Kodvian.Core.Application.Finances.Abstractions;

public class FinanceSetupDto
{
    public DateOnly? StartDate { get; set; }
    public decimal? OpeningArs { get; set; }
    public decimal? OpeningUsd { get; set; }
    public bool HistoryComplete { get; set; }
    public Guid Version { get; set; }
}
public record PartnerDto(Guid Id, string FullName, bool IsActive)
{
    public string? Email { get; init; }
    public string Source { get; init; } = "Manual";
    public Guid? PersonId { get; init; }
    public decimal ParticipationPercentage { get; init; }
}
public class PartnerParticipationRequest { public IReadOnlyCollection<PartnerParticipationItem> Partners { get; set; } = []; }
public record PartnerParticipationItem(Guid PartnerId, decimal Percentage, bool IsActive);
public record IncomeExpenseCoverageDto(Guid ExpenseMovementId, string Description, decimal Amount);
public record IncomePartnerDistributionDto(Guid PartnerId, string PartnerName, decimal Percentage, decimal Amount);
public class IncomeAllocationDto
{
    public Guid IncomeMovementId { get; set; }
    public DateOnly SettlementDate { get; set; }
    public string Description { get; set; } = "";
    public string Currency { get; set; } = "ARS";
    public decimal GrossAmount { get; set; }
    public decimal ExpensesCovered { get; set; }
    public decimal CapitalizedAmount { get; set; }
    public decimal DistributableAmount { get; set; }
    public bool SharesConfigured { get; set; }
    public IReadOnlyCollection<IncomeExpenseCoverageDto> Expenses { get; set; } = [];
    public IReadOnlyCollection<IncomePartnerDistributionDto> Partners { get; set; } = [];
}
public record CapitalAllocationMonthDto(int Year, int Month, decimal GrossIncome, decimal ExpensesCovered, decimal Capitalized, decimal Distributable, decimal CumulativeCapital);
public record PendingExpenseForAllocationDto(Guid ExpenseMovementId, string Description, DateOnly MovementDate, decimal RemainingAmount);
public class CapitalAllocationCurrencyDto
{
    public string Currency { get; set; } = "ARS";
    public decimal OpeningCapital { get; set; }
    public decimal CapitalizedInYear { get; set; }
    public decimal DistributableInYear { get; set; }
    public decimal ExpensesAwaitingCoverage { get; set; }
    public IReadOnlyCollection<PendingExpenseForAllocationDto> PendingExpenses { get; set; } = [];
    public IReadOnlyCollection<CapitalAllocationMonthDto> Months { get; set; } = [];
    public IReadOnlyCollection<IncomeAllocationDto> Receipts { get; set; } = [];
}
public class CapitalAllocationReportDto { public int Year { get; set; } public IReadOnlyCollection<CapitalAllocationCurrencyDto> Currencies { get; set; } = []; }
public class PartnerRequest
{
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Source { get; set; }
    public Guid? PersonId { get; set; }
}
public class PartnerPeopleRequest : Kodvian.Core.Application.Common.Models.PagedRequestDto { public string? Search { get; set; } }
public record PartnerPersonDto(string Source, Guid PersonId, string FullName, string? Email, Guid? RegisteredPartnerId);
public class CurrencyPeriodDto
{
    public string Currency { get; set; } = "ARS";
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Result => Income - Expense;
    public decimal PendingIncome { get; set; }
    public decimal PendingExpense { get; set; }
}
public class FinancePeriodSummaryDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public IReadOnlyCollection<CurrencyPeriodDto> Currencies { get; set; } = [];
}
public class CurrencyOverviewDto : CurrencyPeriodDto
{
    public decimal Contributions { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal Reimbursements { get; set; }
    public decimal CashChange { get; set; }
    public decimal RecordedCashBalance { get; set; }
    public decimal? Balance { get; set; }
}
public class FinanceMonthDto : CurrencyOverviewDto { public int Year { get; set; } public int Month { get; set; } }
public record ExpenseCategoryDto(string Currency, string Category, decimal Amount, Guid CategoryId);
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
    Task<FinancePeriodSummaryDto> GetPeriodSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<Kodvian.Core.Application.Common.Models.PagedResultDto<Kodvian.Core.Application.Developers.Dtos.ContractLedgerDto>> TeamObligationsAsync(int year, Kodvian.Core.Application.Common.Models.PagedRequestDto request, CancellationToken ct);
    Task<FinanceOverviewDto> GetAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<CapitalAllocationReportDto> GetCapitalAllocationsAsync(int year, CancellationToken ct);
    Task<FinanceSetupDto> SetupAsync(FinanceSetupDto request, CancellationToken ct);
    Task<IReadOnlyCollection<PartnerDto>> PartnersAsync(CancellationToken ct);
    Task<Kodvian.Core.Application.Common.Models.PagedResultDto<PartnerPersonDto>> PartnerPeopleAsync(PartnerPeopleRequest request, CancellationToken ct);
    Task<PartnerDto> SavePartnerAsync(Guid? id, PartnerRequest request, CancellationToken ct);
    Task SavePartnerParticipationsAsync(PartnerParticipationRequest request, CancellationToken ct);
    Task<Guid> ExchangeAsync(ExchangeRequest request, CancellationToken ct);
    Task CancelExchangeAsync(Guid id, CancellationToken ct);
}
