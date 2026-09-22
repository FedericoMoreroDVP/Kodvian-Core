namespace Kodvian.Core.Application.Developers.Dtos;

public class ContractLedgerDto
{
    public Guid ContractId { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public Guid DeveloperId { get; set; }
    public string DeveloperName { get; set; } = string.Empty;
    public string PaymentMode { get; set; } = string.Empty;
    public decimal? Percentage { get; set; }
    public decimal? AgreedAmount { get; set; }
    public string? Currency { get; set; }
    public bool NeedsReview { get; set; }
    public IReadOnlyCollection<ContractCurrencyLedgerDto> Currencies { get; set; } = [];
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalBalance { get; set; }
    public IReadOnlyCollection<ContractLedgerMonthDto> Months { get; set; } = Array.Empty<ContractLedgerMonthDto>();
}

public class ContractCurrencyLedgerDto
{
    public string Currency { get; set; } = "ARS";
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalBalance => TotalDue - TotalPaid;
    public IReadOnlyCollection<ContractLedgerMonthDto> Months { get; set; } = [];
}
