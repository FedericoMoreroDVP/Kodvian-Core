using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Finances;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class FinanceOverviewService(KodvianDbContext db, ICurrentUser currentUser) : IFinanceOverviewService
{
    public async Task<Kodvian.Core.Application.Common.Models.PagedResultDto<Kodvian.Core.Application.Developers.Dtos.ContractLedgerDto>> TeamObligationsAsync(int year, Kodvian.Core.Application.Common.Models.PagedRequestDto request, CancellationToken ct)
    {
        if (year is < 2000 or > 2100) throw new ArgumentException("Año inválido");
        var query = db.ProjectDeveloperContracts.AsNoTracking();
        var total = await query.CountAsync(ct);
        var ids = await query.OrderBy(x => x.Project!.Nombre).ThenBy(x => x.Id).Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).Select(x => x.Id).ToListAsync(ct);
        var items = new List<Kodvian.Core.Application.Developers.Dtos.ContractLedgerDto>();
        var ledgers = new ProjectDeveloperContractService(db);
        foreach (var id in ids) { var ledger = await ledgers.GetLedgerAsync(id, year, ct); if (ledger != null) items.Add(ledger); }
        return new() { Items = items, TotalCount = total, PageNumber = request.PageNumber, PageSize = request.PageSize };
    }
    private IQueryable<FinancialMovement> Settled(DateOnly cutoff) => db.FinancialMovements.AsNoTracking()
        .Where(x => x.Activo && x.SettlementDate.HasValue && x.SettlementDate <= cutoff
            && ((x.MovementType == FinancialMovementType.Ingreso && x.Status == FinancialMovementStatus.Cobrado)
                || (x.MovementType == FinancialMovementType.Egreso && x.Status == FinancialMovementStatus.Pagado)));

    public async Task<FinancePeriodSummaryDto> GetPeriodSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        FinanceRules.Date(from); FinanceRules.Date(to);
        if (from > to) throw new ArgumentException("El período es inválido");
        await using var snapshot = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = to < today ? to : today;
        // The daily screens need only current-period totals, not the complete history,
        // opening balances, partners, categories or monthly evolution.
        var totals = await Settled(cutoff).Where(x => x.SettlementDate >= from && x.Nature == "Operacion")
            .GroupBy(x => new { x.Currency, x.MovementType })
            .Select(g => new { g.Key.Currency, g.Key.MovementType, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var pending = await db.FinancialMovements.AsNoTracking()
            .Where(x => x.Activo && x.MovementDate >= from && x.MovementDate <= to
                && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido))
            .GroupBy(x => new { x.Currency, x.MovementType })
            .Select(g => new { g.Key.Currency, g.Key.MovementType, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var result = new FinancePeriodSummaryDto
        {
            From = from, To = to,
            Currencies = FinanceRules.Currencies.Select(currency => new CurrencyPeriodDto
            {
                Currency = currency,
                Income = totals.Where(x => x.Currency == currency && x.MovementType == FinancialMovementType.Ingreso).Sum(x => x.Amount),
                Expense = totals.Where(x => x.Currency == currency && x.MovementType == FinancialMovementType.Egreso).Sum(x => x.Amount),
                PendingIncome = pending.Where(x => x.Currency == currency && x.MovementType == FinancialMovementType.Ingreso).Sum(x => x.Amount),
                PendingExpense = pending.Where(x => x.Currency == currency && x.MovementType == FinancialMovementType.Egreso).Sum(x => x.Amount)
            }).ToArray()
        };
        if (snapshot != null) await snapshot.CommitAsync(ct);
        return result;
    }

    public async Task<FinanceOverviewDto> GetAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        // Every aggregate in this response must describe the same database snapshot.
        await using var snapshot = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (from.HasValue) FinanceRules.Date(from.Value);
        if (to.HasValue) FinanceRules.Date(to.Value);
        if (from > to) throw new ArgumentException("El período es inválido");
        var cutoff = to.HasValue && to < today ? to.Value : today;
        var settings = await db.FinanceSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        // Opening-balance configuration must not hide previously registered receipts.
        var start = from;
        var settled = Settled(cutoff);
        var period = start.HasValue ? settled.Where(x => x.SettlementDate >= start) : settled;
        var totals = await period.GroupBy(x => new { x.Currency, x.Nature, x.Funding, x.MovementType, Year = x.SettlementDate!.Value.Year, Month = x.SettlementDate.Value.Month })
            .Select(g => new Aggregate { Currency = g.Key.Currency, Nature = g.Key.Nature, Funding = g.Key.Funding,
                Type = g.Key.MovementType, Year = g.Key.Year, Month = g.Key.Month, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var pending = await db.FinancialMovements.AsNoTracking()
            .Where(x => x.Activo && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido)
                && (!to.HasValue || x.MovementDate <= to) && (!start.HasValue || x.MovementDate >= start))
            .GroupBy(x => new { x.Currency, x.MovementType })
            .Select(g => new { g.Key.Currency, g.Key.MovementType, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var anchor = settings?.StartDate;
        var recordedCash = await settled.Where(x => x.Funding == "Empresa")
            .GroupBy(x => new { x.Currency, Year = x.SettlementDate!.Value.Year, Month = x.SettlementDate.Value.Month })
            .Select(g => new { g.Key.Currency, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.MovementType == FinancialMovementType.Ingreso ? x.Amount : -x.Amount) }).ToListAsync(ct);
        var cash = await settled.Where(x => x.Funding == "Empresa" && anchor.HasValue && x.SettlementDate >= anchor)
            .GroupBy(x => new { x.Currency, Year = x.SettlementDate!.Value.Year, Month = x.SettlementDate.Value.Month })
            .Select(g => new { g.Key.Currency, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.MovementType == FinancialMovementType.Ingreso ? x.Amount : -x.Amount) }).ToListAsync(ct);

        decimal? Balance(string currency, int year, int month)
        {
            var initial = currency == "ARS" ? settings?.OpeningArs : settings?.OpeningUsd;
            if (settings?.StartDate is null || !initial.HasValue || settings.StartDate > cutoff
                || settings.StartDate.Value.Year * 100 + settings.StartDate.Value.Month > year * 100 + month) return null;
            return initial + cash.Where(x => x.Currency == currency && x.Year * 100 + x.Month <= year * 100 + month).Sum(x => x.Amount);
        }
        var currencies = FinanceRules.Currencies.Select(currency =>
        {
            var result = Sum(totals.Where(x => x.Currency == currency), new CurrencyOverviewDto { Currency = currency });
            result.PendingIncome = pending.Where(x => x.Currency == currency && x.MovementType == FinancialMovementType.Ingreso).Sum(x => x.Amount);
            result.PendingExpense = pending.Where(x => x.Currency == currency && x.MovementType == FinancialMovementType.Egreso).Sum(x => x.Amount);
            result.Balance = Balance(currency, cutoff.Year, cutoff.Month);
            result.RecordedCashBalance = recordedCash.Where(x => x.Currency == currency).Sum(x => x.Amount);
            return result;
        }).ToArray();
        var months = new List<FinanceMonthDto>();
        var firstMonth = start ?? await period.Select(x => (DateOnly?)x.SettlementDate).MinAsync(ct);
        if (firstMonth.HasValue)
            for (var date = new DateOnly(firstMonth.Value.Year, firstMonth.Value.Month, 1); date <= cutoff; date = date.AddMonths(1))
                foreach (var currency in FinanceRules.Currencies)
                {
                    var result = Sum(totals.Where(x => x.Currency == currency && x.Year == date.Year && x.Month == date.Month),
                        new FinanceMonthDto { Currency = currency, Year = date.Year, Month = date.Month });
                    result.Balance = Balance(currency, date.Year, date.Month);
                    result.RecordedCashBalance = recordedCash.Where(x => x.Currency == currency && x.Year * 100 + x.Month <= date.Year * 100 + date.Month).Sum(x => x.Amount);
                    months.Add(result);
                }
        var expenses = await period.Where(x => x.Nature == "Operacion" && x.MovementType == FinancialMovementType.Egreso)
            .GroupBy(x => new { x.Currency, x.CategoryId, Category = x.Category!.Name }).Select(g => new ExpenseCategoryDto(g.Key.Currency, g.Key.Category, g.Sum(x => x.Amount), g.Key.CategoryId)).ToListAsync(ct);
        var partners = await settled.Where(x => x.PartnerId != null)
            .GroupBy(x => new { x.PartnerId, Name = x.Partner!.FullName, x.Currency })
            .Select(g => new PartnerBalanceDto(g.Key.PartnerId!.Value, g.Key.Name, g.Key.Currency,
                g.Sum(x => x.Nature == "AporteSocio" || x.Funding == "SocioAporte" ? x.Amount : 0),
                g.Sum(x => x.Nature == "RetiroSocio" ? x.Amount : 0),
                g.Sum(x => x.Funding == "SocioReintegrable" ? x.Amount : 0),
                g.Sum(x => x.Nature == "ReintegroSocio" ? x.Amount : 0))).ToListAsync(ct);
        var response = new FinanceOverviewDto
        {
            Setup = Map(settings), From = start, To = cutoff, Currencies = currencies, Months = months, Expenses = expenses, Partners = partners,
            EstimatedDateCount = await settled.CountAsync(x => x.SettlementDateEstimated, ct),
            UnclassifiedPayments = await db.DeveloperPayments.CountAsync(x => x.Activo && (x.Currency == null || x.AppliedCurrency == null || x.AppliedAmount == null || x.FinancialMovementId == null), ct)
        };
        if (snapshot != null) await snapshot.CommitAsync(ct);
        return response;
    }

    private sealed class Aggregate
    {
        public string Currency { get; set; } = ""; public string Nature { get; set; } = ""; public string Funding { get; set; } = "";
        public FinancialMovementType Type { get; set; } public int Year { get; set; } public int Month { get; set; } public decimal Amount { get; set; }
    }
    private static T Sum<T>(IEnumerable<Aggregate> rows, T result) where T : CurrencyOverviewDto
    {
        foreach (var row in rows)
        {
            if (row.Nature == "Operacion") { if (row.Type == FinancialMovementType.Ingreso) result.Income += row.Amount; else result.Expense += row.Amount; }
            if (row.Nature == "AporteSocio" || row.Funding == "SocioAporte") result.Contributions += row.Amount;
            if (row.Nature == "RetiroSocio") result.Withdrawals += row.Amount;
            if (row.Nature == "ReintegroSocio") result.Reimbursements += row.Amount;
            if (row.Funding == "Empresa") result.CashChange += row.Type == FinancialMovementType.Ingreso ? row.Amount : -row.Amount;
        }
        return result;
    }
    public async Task<FinanceSetupDto> SetupAsync(FinanceSetupDto request, CancellationToken ct)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(db, ct);
        if (request.StartDate.HasValue) FinanceRules.Date(request.StartDate.Value);
        if (request.StartDate > DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("El punto de partida no puede ser futuro");
        foreach (var value in new[] { request.OpeningArs, request.OpeningUsd })
            if (value.HasValue && (decimal.Round(value.Value, 2) != value || Math.Abs(value.Value) > 9999999999999999.99m)) throw new ArgumentException("Saldo inicial inválido");
        if ((request.OpeningArs.HasValue || request.OpeningUsd.HasValue) && !request.StartDate.HasValue)
            throw new ArgumentException("Indica la fecha a la que corresponden los saldos iniciales");
        if (request.HistoryComplete && (!request.StartDate.HasValue || !request.OpeningArs.HasValue || !request.OpeningUsd.HasValue))
            throw new ArgumentException("Completa fecha y ambos saldos antes de confirmar el historial");
        var entity = await db.FinanceSettings.SingleOrDefaultAsync(ct);
        if ((entity?.Version ?? Guid.Empty) != request.Version) throw new ArgumentException("La configuración cambió. Actualiza y vuelve a intentar.");
        if (entity is null) { entity = new FinanceSettings(); db.FinanceSettings.Add(entity); }
        entity.StartDate = request.StartDate; entity.OpeningArs = request.OpeningArs; entity.OpeningUsd = request.OpeningUsd;
        entity.HistoryComplete = request.HistoryComplete; entity.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); return Map(entity);
    }
    private static FinanceSetupDto Map(FinanceSettings? x) => new() { StartDate = x?.StartDate, OpeningArs = x?.OpeningArs, OpeningUsd = x?.OpeningUsd, HistoryComplete = x?.HistoryComplete ?? false, Version = x?.Version ?? Guid.Empty };
    public async Task<IReadOnlyCollection<PartnerDto>> PartnersAsync(CancellationToken ct) => await db.Partners.AsNoTracking().OrderBy(x => x.FullName)
        .Select(x => new PartnerDto(x.Id, x.FullName, x.Activo)).ToListAsync(ct);
    public async Task<PartnerDto> SavePartnerAsync(Guid? id, PartnerRequest request, CancellationToken ct)
    {
        var name = request.FullName?.Trim() ?? "";
        if (name.Length is 0 or > 160) throw new ArgumentException("Indica el nombre del socio (hasta 160 caracteres)");
        var entity = id.HasValue ? await db.Partners.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Socio no encontrado") : new Partner();
        if (!id.HasValue) db.Partners.Add(entity);
        entity.FullName = name; entity.Activo = request.IsActive; entity.FechaActualizacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return new(entity.Id, entity.FullName, entity.Activo);
    }
    public async Task<Guid> ExchangeAsync(ExchangeRequest request, CancellationToken ct)
    {
        FinanceRules.Currency(request.FromCurrency); FinanceRules.Currency(request.ToCurrency); FinanceRules.Money(request.FromAmount); FinanceRules.Money(request.ToAmount); FinanceRules.Date(request.Date);
        if (request.Notes?.Length > 1000) throw new ArgumentException("Las observaciones no pueden superar 1000 caracteres");
        if (request.RequestId == Guid.Empty || request.FromCurrency == request.ToCurrency || request.Date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("Cambio de moneda inválido");
        await using var tx = await FinanceWriteScope.BeginAsync(db, ct);
        var existing = await db.FinancialMovements.Where(x => x.ExchangeId == request.RequestId).ToListAsync(ct);
        if (existing.Count > 0)
        {
            if (existing.Count != 2 || existing.Any(x => x.Status == FinancialMovementStatus.Anulado)
                || !existing.Any(x => x.MovementType == FinancialMovementType.Egreso && x.Amount == request.FromAmount && x.Currency == request.FromCurrency && x.SettlementDate == request.Date)
                || !existing.Any(x => x.MovementType == FinancialMovementType.Ingreso && x.Amount == request.ToAmount && x.Currency == request.ToCurrency))
                throw new ArgumentException("Este identificador ya corresponde a otro cambio de moneda");
            return request.RequestId;
        }
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        foreach (var incoming in new[] { false, true })
        {
            var type = incoming ? FinancialMovementType.Ingreso : FinancialMovementType.Egreso;
            var category = await SystemCategoryAsync(db, type, "Cambio de moneda", ct);
            db.FinancialMovements.Add(new FinancialMovement { Currency = incoming ? request.ToCurrency : request.FromCurrency,
                Amount = incoming ? request.ToAmount : request.FromAmount, MovementType = type, Category = category, Nature = "CambioMoneda",
                MovementDate = request.Date, SettlementDate = request.Date, Status = incoming ? FinancialMovementStatus.Cobrado : FinancialMovementStatus.Pagado,
                Description = $"Cambio {request.FromCurrency} → {request.ToCurrency}", Notes = request.Notes, ExchangeId = request.RequestId, CreatedById = actor });
        }
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); return request.RequestId;
    }
    public async Task CancelExchangeAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(db, ct);
        var rows = await db.FinancialMovements.Where(x => x.ExchangeId == id).ToListAsync(ct);
        if (rows.Count != 2) throw new KeyNotFoundException("Cambio no encontrado");
        foreach (var row in rows) { row.Status = FinancialMovementStatus.Anulado; row.Version = Guid.NewGuid(); row.FechaActualizacion = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct);
    }
    public static async Task<FinancialCategory> SystemCategoryAsync(KodvianDbContext db, FinancialMovementType type, string name, CancellationToken ct)
    {
        var category = await db.FinancialCategories.FirstOrDefaultAsync(x => x.Name == name && x.MovementType == type, ct);
        if (category != null) return category;
        category = new FinancialCategory { Name = name, MovementType = type, IsActive = true }; db.FinancialCategories.Add(category); return category;
    }
}
