namespace Kodvian.Core.Application.Ai.Dtos;

public sealed class AiUsageSettingsDto
{
    public decimal? InputUsdPerMillion { get; set; }
    public decimal? OutputUsdPerMillion { get; set; }
    public decimal? FastInputUsdPerMillion { get; set; }
    public decimal? FastOutputUsdPerMillion { get; set; }
    public decimal? MonthlyBudgetUsd { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class AiBudgetStatusDto
{
    public decimal? MonthlyBudgetUsd { get; init; }
    public decimal MonthSpendUsd { get; init; }
    public decimal? UsedPercent { get; init; }
    public bool PricesConfigured { get; init; }
    /// <summary>ok, warning (80% o más) o exceeded (100% o más: el asistente queda pausado hasta el mes siguiente o hasta ampliar el tope).</summary>
    public string Level { get; init; } = "ok";
}

public sealed class AiUsageReportDto
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public AiUsageTotalsDto Totals { get; init; } = new();
    public IReadOnlyCollection<AiUsageDayDto> Days { get; init; } = [];
    public IReadOnlyCollection<AiUsageModelDto> ByModel { get; init; } = [];
    public IReadOnlyCollection<AiUsageCountDto> TopTools { get; init; } = [];
    public IReadOnlyCollection<AiUsageCountDto> Errors { get; init; } = [];
    public IReadOnlyCollection<AiUsageCountDto> Actions { get; init; } = [];
    public int PositiveFeedback { get; init; }
    public int NegativeFeedback { get; init; }
    public int ActiveUsers { get; init; }
    public AiBudgetStatusDto Budget { get; init; } = new();
    public AiUsageSettingsDto Settings { get; init; } = new();
}

public sealed class AiUsageTotalsDto
{
    public int Runs { get; init; }
    public int FailedRuns { get; init; }
    public int PartialRuns { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
}

public sealed class AiUsageDayDto
{
    public DateOnly Date { get; init; }
    public int Runs { get; init; }
    public int FailedRuns { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
}

public sealed class AiUsageModelDto
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int Runs { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
}

public sealed class AiUsageCountDto
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}
