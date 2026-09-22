using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Finances.Requests;

public class FinancialMovementListRequestDto : PagedRequestDto
{
    public string? Currency { get; set; }
    public string? Nature { get; set; }
    public Guid? ProjectId { get; set; }
    public bool UnlinkedOnly { get; set; }
    public bool UseSettlementDate { get; set; }
    public decimal? ExactAmount { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? MovementType { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? ProviderId { get; set; }
    public string? Status { get; set; }
}
