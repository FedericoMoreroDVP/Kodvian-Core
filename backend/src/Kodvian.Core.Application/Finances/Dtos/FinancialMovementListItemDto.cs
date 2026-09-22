namespace Kodvian.Core.Application.Finances.Dtos;

public class FinancialMovementListItemDto
{
    public Guid Id { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public string Nature { get; set; } = "Operacion";
    public Guid? ExchangeId { get; set; }
    public Guid? DeveloperPaymentId { get; set; }
    public DateOnly? SettlementDate { get; set; }
    public DateOnly MovementDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? ClientName { get; set; }
    public string? ProviderName { get; set; }
}
