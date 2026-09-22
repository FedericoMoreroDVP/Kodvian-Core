using Kodvian.Core.Domain.Enums;

namespace Kodvian.Core.Domain.Entities;

public class FinancialMovement : BaseEntity
{
    public FinancialMovementType MovementType { get; set; } = FinancialMovementType.Ingreso;
    public Guid CategoryId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? ProviderId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public string Nature { get; set; } = "Operacion";
    public string Funding { get; set; } = "Empresa";
    public Guid? PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public DateOnly? SettlementDate { get; set; }
    public bool SettlementDateEstimated { get; set; }
    public Guid? ExchangeId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DeveloperPayment? DeveloperPayment { get; set; }
    public DateOnly MovementDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? DueDate { get; set; }
    public FinancialMovementStatus Status { get; set; } = FinancialMovementStatus.Pendiente;
    public string? PaymentMethod { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedById { get; set; }

    public FinancialCategory? Category { get; set; }
    public Client? Client { get; set; }
    public Provider? Provider { get; set; }
    public Project? Project { get; set; }
    public User? CreatedBy { get; set; }
    public ICollection<DocumentFile> Documents { get; set; } = new List<DocumentFile>();
}
