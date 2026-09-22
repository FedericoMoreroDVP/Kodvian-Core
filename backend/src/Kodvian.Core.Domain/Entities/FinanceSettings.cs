namespace Kodvian.Core.Domain.Entities;

public class FinanceSettings
{
    public int Id { get; set; } = 1;
    public DateOnly? StartDate { get; set; }
    public decimal? OpeningArs { get; set; }
    public decimal? OpeningUsd { get; set; }
    public bool HistoryComplete { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
