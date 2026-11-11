using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class FinancialIncomeAllocationService(KodvianDbContext db)
{
    public async Task AllocateAsync(Guid incomeMovementId, CancellationToken ct)
    {
        var income = await db.FinancialMovements.SingleOrDefaultAsync(x => x.Id == incomeMovementId, ct);
        if (income is null || !income.Activo || income.MovementType != FinancialMovementType.Ingreso
            || income.Nature != "Operacion" || income.Funding != "Empresa"
            || income.Status != FinancialMovementStatus.Cobrado || income.SettlementDate is null)
            return;
        if (await db.FinancialIncomeAllocations.AnyAsync(x => x.IncomeMovementId == income.Id, ct)) return;
        var incomeRegisteredAt = income.FechaActualizacion ?? income.FechaCreacion;

        var expenses = await db.FinancialMovements.Where(x => x.Activo && x.MovementType == FinancialMovementType.Egreso
                && x.Nature == "Operacion" && x.Funding == "Empresa" && x.Currency == income.Currency
                && x.FechaCreacion <= incomeRegisteredAt
                && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido
                    || x.Status == FinancialMovementStatus.Pagado && db.FinancialIncomeExpenseApplications.Any(a =>
                        a.ExpenseMovementId == x.Id && a.Allocation.FechaCreacion <= incomeRegisteredAt)))
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.MovementDate).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Amount, x.Description })
            .ToListAsync(ct);

        var expenseIds = expenses.Select(x => x.Id).ToArray();
        var previouslyCovered = expenseIds.Length == 0
            ? new Dictionary<Guid, decimal>()
            : await db.FinancialIncomeExpenseApplications
                .Where(x => expenseIds.Contains(x.ExpenseMovementId)
                    && x.Allocation.FechaCreacion <= incomeRegisteredAt)
                .GroupBy(x => x.ExpenseMovementId)
                .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);

        var allocation = new FinancialIncomeAllocation
        {
            IncomeMovementId = income.Id,
            Currency = income.Currency,
            GrossAmount = income.Amount
        };
        var remaining = income.Amount;
        foreach (var expense in expenses)
        {
            if (remaining <= 0) break;
            var available = Math.Max(0, expense.Amount - previouslyCovered.GetValueOrDefault(expense.Id));
            var covered = Math.Min(remaining, available);
            if (covered <= 0) continue;
            allocation.ExpenseApplications.Add(new FinancialIncomeExpenseApplication
            {
                AllocationId = allocation.Id,
                ExpenseMovementId = expense.Id,
                Amount = covered
            });
            remaining -= covered;
        }

        allocation.ExpensesCovered = income.Amount - remaining;
        allocation.CapitalizedAmount = decimal.Round(remaining * 0.10m, 2, MidpointRounding.AwayFromZero);
        allocation.DistributableAmount = remaining - allocation.CapitalizedAmount;

        var partners = await db.Partners.AsNoTracking().Where(x => x.Activo).OrderBy(x => x.Id)
            .Select(x => new PartnerShare(x.Id, x.Developer != null ? x.Developer.FullName : x.User != null ? x.User.FullName : x.FullName, x.ParticipationPercentage))
            .ToListAsync(ct);
        allocation.SharesConfigured = partners.Count > 0 && partners.Sum(x => x.ParticipationPercentage) == 100m;
        if (allocation.SharesConfigured)
            AddPartnerDistributions(allocation, partners);

        db.FinancialIncomeAllocations.Add(allocation);
        await db.SaveChangesAsync(ct);
    }

    public async Task CompletePendingDistributionsAsync(CancellationToken ct)
    {
        // Partner shares have been saved before this call. Clear tracked snapshots so
        // allocations created earlier in the same request are reloaded consistently.
        db.ChangeTracker.Clear();
        var partners = await db.Partners.Where(x => x.Activo).OrderBy(x => x.Id)
            .Select(x => new PartnerShare(x.Id, x.Developer != null ? x.Developer.FullName : x.User != null ? x.User.FullName : x.FullName, x.ParticipationPercentage))
            .ToListAsync(ct);
        if (partners.Count == 0 || partners.Sum(x => x.ParticipationPercentage) != 100m) return;
        var allocations = await db.FinancialIncomeAllocations.AsNoTracking()
            .Where(x => !x.SharesConfigured && !x.PartnerDistributions.Any()).ToListAsync(ct);
        foreach (var allocation in allocations)
            db.FinancialIncomePartnerDistributions.AddRange(BuildPartnerDistributions(allocation.Id, allocation.DistributableAmount, partners));
        if (allocations.Count > 0) await db.SaveChangesAsync(ct);
    }

    private static void AddPartnerDistributions(FinancialIncomeAllocation allocation, IReadOnlyList<PartnerShare> partners)
    {
        foreach (var distribution in BuildPartnerDistributions(allocation.Id, allocation.DistributableAmount, partners))
            allocation.PartnerDistributions.Add(distribution);
    }

    private static List<FinancialIncomePartnerDistribution> BuildPartnerDistributions(Guid allocationId, decimal distributableAmount, IReadOnlyList<PartnerShare> partners)
    {
        var distributions = new List<FinancialIncomePartnerDistribution>();
        decimal allocated = 0;
        for (var i = 0; i < partners.Count; i++)
        {
            var partner = partners[i];
            var amount = i == partners.Count - 1 ? distributableAmount - allocated
                : decimal.Round(distributableAmount * partner.ParticipationPercentage / 100m, 2, MidpointRounding.AwayFromZero);
            allocated += amount;
            distributions.Add(new FinancialIncomePartnerDistribution
            {
                AllocationId = allocationId,
                PartnerId = partner.Id,
                PartnerName = partner.Name,
                Percentage = partner.ParticipationPercentage,
                Amount = amount
            });
        }
        return distributions;
    }

    private sealed record PartnerShare(Guid Id, string Name, decimal ParticipationPercentage);
}
