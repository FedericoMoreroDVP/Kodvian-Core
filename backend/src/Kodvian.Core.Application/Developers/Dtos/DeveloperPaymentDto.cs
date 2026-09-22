using Kodvian.Core.Application.Common.Files;

namespace Kodvian.Core.Application.Developers.Dtos;

public class DeveloperPaymentDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? AppliedCurrency { get; set; }
    public decimal? AppliedAmount { get; set; }
    public decimal? ExchangeRate => AppliedAmount > 0 ? Amount / AppliedAmount : null;
    public Guid? FinancialMovementId { get; set; }
    public Guid Version { get; set; }
    public bool IsActive { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyCollection<FileMetadataDto> Receipts { get; set; } = Array.Empty<FileMetadataDto>();
}
