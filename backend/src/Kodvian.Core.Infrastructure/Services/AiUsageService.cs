using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>Tablero de uso del asistente, precios por modelo y tope de gasto mensual propio.</summary>
public sealed class AiUsageService(KodvianDbContext dbContext) : IAiUsageService
{
    internal const decimal WarningRatio = 0.8m;
    private static readonly TimeZoneInfo BuenosAires = AiSystemPromptBuilder.BuenosAiresTimeZone;

    public async Task<AiUsageReportDto> GetReportAsync(int days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > 90) throw new ArgumentException("El período debe ser de 1 a 90 días.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BuenosAires));
        var from = today.AddDays(-(days - 1));
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), BuenosAires);

        var runs = await dbContext.AiRuns.AsNoTracking().Where(x => x.StartedAtUtc >= fromUtc)
            .Select(x => new { x.StartedAtUtc, x.Status, x.InputTokens, x.OutputTokens, x.EstimatedCostUsd, x.Provider, x.Model, x.ActorUserId })
            .ToListAsync(cancellationToken);
        var audit = await dbContext.AiAuditEvents.AsNoTracking().Where(x => x.CreatedAtUtc >= fromUtc)
            .Select(x => new { x.ToolName, x.Decision, x.ReasonCode }).ToListAsync(cancellationToken);
        var actions = await dbContext.AiPendingActions.AsNoTracking().Where(x => x.CreatedAtUtc >= fromUtc)
            .GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var feedback = await dbContext.AiMessages.AsNoTracking().Where(x => x.FeedbackAtUtc >= fromUtc && x.Feedback != null)
            .GroupBy(x => x.Feedback).Select(g => new { Value = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BuenosAires));
        var byDay = runs.GroupBy(x => LocalDate(x.StartedAtUtc)).ToDictionary(g => g.Key);
        var dayRows = Enumerable.Range(0, days).Select(offset => from.AddDays(offset)).Select(date => byDay.TryGetValue(date, out var g)
            ? new AiUsageDayDto { Date = date, Runs = g.Count(), FailedRuns = g.Count(x => x.Status == "Failed"), InputTokens = g.Sum(x => (long)x.InputTokens), OutputTokens = g.Sum(x => (long)x.OutputTokens), CostUsd = g.Sum(x => x.EstimatedCostUsd ?? 0) }
            : new AiUsageDayDto { Date = date }).ToArray();

        var settings = await LoadSettingsAsync(dbContext, cancellationToken);
        return new AiUsageReportDto
        {
            From = from,
            To = today,
            Totals = new AiUsageTotalsDto
            {
                Runs = runs.Count, FailedRuns = runs.Count(x => x.Status == "Failed"), PartialRuns = runs.Count(x => x.Status == "Partial"),
                InputTokens = runs.Sum(x => (long)x.InputTokens), OutputTokens = runs.Sum(x => (long)x.OutputTokens), CostUsd = runs.Sum(x => x.EstimatedCostUsd ?? 0)
            },
            Days = dayRows,
            ByModel = runs.GroupBy(x => new { x.Provider, x.Model }).OrderByDescending(g => g.Count()).Select(g => new AiUsageModelDto
            {
                Provider = g.Key.Provider, Model = g.Key.Model, Runs = g.Count(), InputTokens = g.Sum(x => (long)x.InputTokens),
                OutputTokens = g.Sum(x => (long)x.OutputTokens), CostUsd = g.Sum(x => x.EstimatedCostUsd ?? 0)
            }).ToArray(),
            TopTools = audit.Where(x => x.ToolName != null && x.Decision is "Allowed" or "Proposed").GroupBy(x => x.ToolName!)
                .OrderByDescending(g => g.Count()).Take(10).Select(g => new AiUsageCountDto { Name = g.Key, Count = g.Count() }).ToArray(),
            Errors = audit.Where(x => x.Decision is "Failed" or "Denied").GroupBy(x => x.ToolName is null ? x.ReasonCode : $"{x.ToolName}: {x.ReasonCode}")
                .OrderByDescending(g => g.Count()).Take(10).Select(g => new AiUsageCountDto { Name = g.Key, Count = g.Count() }).ToArray(),
            Actions = actions.OrderByDescending(x => x.Count).Select(x => new AiUsageCountDto { Name = x.Status, Count = x.Count }).ToArray(),
            PositiveFeedback = feedback.Where(x => x.Value == 1).Sum(x => x.Count),
            NegativeFeedback = feedback.Where(x => x.Value == -1).Sum(x => x.Count),
            ActiveUsers = runs.Select(x => x.ActorUserId).Distinct().Count(),
            Budget = await GetBudgetStatusAsync(dbContext, cancellationToken),
            Settings = ToDto(settings)
        };
    }

    public async Task<AiUsageSettingsDto> UpdateSettingsAsync(Guid actorUserId, AiUsageSettingsDto request, CancellationToken cancellationToken)
    {
        static decimal? Price(decimal? value, string field)
        {
            if (value is null) return null;
            if (value is < 0 or > 10_000) throw new ArgumentException($"El precio de {field} no es válido.");
            return Math.Round(value.Value, 4);
        }
        if (request.MonthlyBudgetUsd is < 0 or > 1_000_000) throw new ArgumentException("El tope mensual no es válido.");
        var entity = await dbContext.AiUsageSettings.SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            entity = new AiUsageSettings();
            dbContext.AiUsageSettings.Add(entity);
        }
        entity.InputUsdPerMillion = Price(request.InputUsdPerMillion, "entrada");
        entity.OutputUsdPerMillion = Price(request.OutputUsdPerMillion, "salida");
        entity.FastInputUsdPerMillion = Price(request.FastInputUsdPerMillion, "entrada del modelo rápido");
        entity.FastOutputUsdPerMillion = Price(request.FastOutputUsdPerMillion, "salida del modelo rápido");
        entity.MonthlyBudgetUsd = request.MonthlyBudgetUsd is null ? null : Math.Round(request.MonthlyBudgetUsd.Value, 2);
        entity.UpdatedByUserId = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public Task<AiBudgetStatusDto> GetBudgetStatusAsync(CancellationToken cancellationToken) => GetBudgetStatusAsync(dbContext, cancellationToken);

    internal static async Task<AiBudgetStatusDto> GetBudgetStatusAsync(KodvianDbContext db, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(db, ct);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BuenosAires);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(nowLocal.Year, nowLocal.Month, 1), BuenosAires);
        var spend = await db.AiRuns.AsNoTracking().Where(x => x.StartedAtUtc >= monthStartUtc && x.EstimatedCostUsd != null).SumAsync(x => x.EstimatedCostUsd ?? 0, ct);
        var pricesConfigured = settings?.InputUsdPerMillion is not null && settings.OutputUsdPerMillion is not null;
        var budget = settings?.MonthlyBudgetUsd;
        decimal? percent = budget is > 0 ? Math.Round(spend / budget.Value * 100, 1) : null;
        var level = budget is > 0 && pricesConfigured
            ? spend >= budget ? "exceeded" : spend >= budget * WarningRatio ? "warning" : "ok"
            : "ok";
        return new AiBudgetStatusDto { MonthlyBudgetUsd = budget, MonthSpendUsd = Math.Round(spend, 4), UsedPercent = percent, PricesConfigured = pricesConfigured, Level = level };
    }

    /// <summary>Costo estimado de una ejecución con los precios vigentes; null si no hay precios cargados.</summary>
    internal static decimal? EstimateCost(AiUsageSettings? settings, AiModelTier tier, int inputTokens, int outputTokens)
    {
        if (settings is null) return null;
        var inputPrice = tier == AiModelTier.Fast ? settings.FastInputUsdPerMillion ?? settings.InputUsdPerMillion : settings.InputUsdPerMillion;
        var outputPrice = tier == AiModelTier.Fast ? settings.FastOutputUsdPerMillion ?? settings.OutputUsdPerMillion : settings.OutputUsdPerMillion;
        if (inputPrice is null || outputPrice is null) return null;
        return Math.Round(inputTokens / 1_000_000m * inputPrice.Value + outputTokens / 1_000_000m * outputPrice.Value, 6);
    }

    internal static Task<AiUsageSettings?> LoadSettingsAsync(KodvianDbContext db, CancellationToken ct) =>
        db.AiUsageSettings.AsNoTracking().SingleOrDefaultAsync(ct);

    private static AiUsageSettingsDto ToDto(AiUsageSettings? entity) => new()
    {
        InputUsdPerMillion = entity?.InputUsdPerMillion, OutputUsdPerMillion = entity?.OutputUsdPerMillion,
        FastInputUsdPerMillion = entity?.FastInputUsdPerMillion, FastOutputUsdPerMillion = entity?.FastOutputUsdPerMillion,
        MonthlyBudgetUsd = entity?.MonthlyBudgetUsd, UpdatedAtUtc = entity?.UpdatedAtUtc
    };
}
