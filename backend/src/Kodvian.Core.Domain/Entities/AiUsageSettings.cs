namespace Kodvian.Core.Domain.Entities;

/// <summary>
/// Precios por millón de tokens (los carga el Administrador según la lista de precios de su proveedor)
/// y tope de gasto mensual propio de Kodvian. Sin precios no se puede estimar el costo ni aplicar el tope.
/// </summary>
public sealed class AiUsageSettings
{
    public int Id { get; set; } = 1;
    public decimal? InputUsdPerMillion { get; set; }
    public decimal? OutputUsdPerMillion { get; set; }
    public decimal? FastInputUsdPerMillion { get; set; }
    public decimal? FastOutputUsdPerMillion { get; set; }
    public decimal? MonthlyBudgetUsd { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
