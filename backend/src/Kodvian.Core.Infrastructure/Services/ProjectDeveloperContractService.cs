using System.Linq.Expressions;
using Kodvian.Core.Application.Developers.Abstractions;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Application.Finances;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class ProjectDeveloperContractService(KodvianDbContext db) : IProjectDeveloperContractService
{
    public async Task<IReadOnlyCollection<ProjectDeveloperContractDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.ProjectDeveloperContracts.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Activo).ThenByDescending(x => x.FechaCreacion).Select(ToDto()).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<DeveloperContractSummaryDto>> GetByDeveloperSummaryAsync(Guid developerId, int year, CancellationToken cancellationToken = default)
    {
        var contracts = await db.ProjectDeveloperContracts.AsNoTracking().Where(x => x.DeveloperId == developerId)
            .OrderByDescending(x => x.Activo).Select(ToDto()).ToListAsync(cancellationToken);
        var result = new List<DeveloperContractSummaryDto>();
        foreach (var contract in contracts)
        {
            var ledger = (await GetLedgerAsync(contract.Id, year, cancellationToken))!;
            result.Add(new DeveloperContractSummaryDto
            {
                ContractId = contract.Id, ProjectId = contract.ProjectId, ProjectName = contract.ProjectName,
                PaymentMode = contract.PaymentMode, Percentage = contract.Percentage, AgreedAmount = contract.AgreedAmount,
                Currency = contract.Currency, Currencies = ledger.Currencies, NeedsReview = ledger.NeedsReview,
                TotalDue = ledger.TotalDue, TotalPaid = ledger.TotalPaid, TotalBalance = ledger.TotalBalance,
                IsUpToDate = !ledger.NeedsReview && ledger.Currencies.All(x => x.TotalBalance <= 0), IsActive = contract.IsActive,
                LastPaymentDate = await db.DeveloperPayments.Where(x => x.ContractId == contract.Id && x.Activo).Select(x => (DateOnly?)x.PaymentDate).MaxAsync(cancellationToken)
            });
        }
        return result;
    }

    public async Task<ProjectDeveloperContractDto> CreateAsync(Guid projectId, ProjectDeveloperContractUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(db, cancellationToken);
        await ValidateAsync(projectId, request, null, cancellationToken);
        var entity = new ProjectDeveloperContract { ProjectId = projectId };
        Apply(entity, request); db.ProjectDeveloperContracts.Add(entity); await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return await db.ProjectDeveloperContracts.Where(x => x.Id == entity.Id).Select(ToDto()).SingleAsync(cancellationToken);
    }
    public async Task<ProjectDeveloperContractDto?> UpdateAsync(Guid id, ProjectDeveloperContractUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(db, cancellationToken);
        var entity = await db.ProjectDeveloperContracts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null) return null;
        await ValidateAsync(entity.ProjectId, request, id, cancellationToken);
        if (await db.DeveloperPayments.AnyAsync(x => x.ContractId == id, cancellationToken)
            && (entity.DeveloperId != request.DeveloperId || entity.PaymentMode.ToString() != request.PaymentMode
                || entity.Currency != null && entity.Currency != request.Currency))
            throw new ArgumentException("Un acuerdo con pagos no puede cambiar de persona, modalidad o moneda. Crea otro acuerdo.");
        Apply(entity, request); entity.FechaActualizacion = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken); if (tx != null) await tx.CommitAsync(cancellationToken);
        return await db.ProjectDeveloperContracts.Where(x => x.Id == id).Select(ToDto()).SingleAsync(cancellationToken);
    }

    public async Task<ContractLedgerDto?> GetLedgerAsync(Guid contractId, int year, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100) throw new ArgumentException("Año inválido");
        var contract = await db.ProjectDeveloperContracts.AsNoTracking().Where(x => x.Id == contractId).Select(ToDto()).SingleOrDefaultAsync(cancellationToken);
        if (contract == null) return null;
        var start = new DateOnly(year, 1, 1); var end = new DateOnly(year, 12, 31);
        if (contract.StartDate > start) start = contract.StartDate;
        if (contract.EndDate.HasValue && contract.EndDate < end) end = contract.EndDate.Value;
        var income = await db.FinancialMovements.AsNoTracking()
            .Where(x => x.ProjectId == contract.ProjectId && x.Activo && x.Nature == "Operacion" && x.MovementType == FinancialMovementType.Ingreso
                && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Cobrado || x.Status == FinancialMovementStatus.Vencido)
                && x.MovementDate >= start && x.MovementDate <= end)
            .GroupBy(x => new { x.Currency, Month = x.MovementDate.Month })
            .Select(g => new { g.Key.Currency, g.Key.Month, Amount = g.Sum(x => x.Amount) }).ToListAsync(cancellationToken);
        var fixedAmount = contract.PaymentMode == "FixedAmount";
        // Fixed agreements are lifetime obligations, not repeated debts every year.
        var payments = await db.DeveloperPayments.AsNoTracking()
            .Where(x => x.ContractId == contractId && x.Activo && x.AppliedCurrency != null && x.AppliedAmount != null && (fixedAmount || x.PeriodYear == year))
            .GroupBy(x => new { Currency = x.AppliedCurrency, Month = x.PeriodMonth })
            .Select(g => new { g.Key.Currency, g.Key.Month, Amount = g.Sum(x => x.AppliedAmount!.Value) }).ToListAsync(cancellationToken);
        var currencies = new List<ContractCurrencyLedgerDto>();
        foreach (var currency in FinanceRules.Currencies)
        {
            var months = new List<ContractLedgerMonthDto>();
            if (fixedAmount)
            {
                var due = contract.Currency == currency ? contract.AgreedAmount ?? 0 : 0;
                var paid = payments.Where(x => x.Currency == currency).Sum(x => x.Amount);
                months.Add(new ContractLedgerMonthDto { Year = contract.StartDate.Year, Month = contract.StartDate.Month, DueAmount = due, PaidAmount = paid, Balance = due - paid });
            }
            else for (var month = 1; month <= 12; month++)
            {
                var basis = income.Where(x => x.Currency == currency && x.Month == month).Sum(x => x.Amount);
                var due = Math.Round(basis * (contract.Percentage ?? 0) / 100, 2, MidpointRounding.AwayFromZero);
                var paid = payments.Where(x => x.Currency == currency && x.Month == month).Sum(x => x.Amount);
                months.Add(new ContractLedgerMonthDto { Year = year, Month = month, ProjectIncomeBase = basis, DueAmount = due, PaidAmount = paid, Balance = due - paid });
            }
            currencies.Add(new ContractCurrencyLedgerDto { Currency = currency, TotalDue = months.Sum(x => x.DueAmount), TotalPaid = months.Sum(x => x.PaidAmount), Months = months });
        }
        var ars = currencies.Single(x => x.Currency == "ARS");
        return new ContractLedgerDto
        {
            ContractId = contractId, ProjectId = contract.ProjectId, ProjectName = contract.ProjectName, DeveloperId = contract.DeveloperId,
            DeveloperName = contract.DeveloperName, PaymentMode = contract.PaymentMode, Percentage = contract.Percentage, AgreedAmount = contract.AgreedAmount,
            Currency = contract.Currency, Currencies = currencies,
            NeedsReview = fixedAmount && contract.Currency == null || await db.DeveloperPayments.AnyAsync(x => x.ContractId == contractId && x.Activo && (x.Currency == null || x.AppliedCurrency == null || x.AppliedAmount == null || x.FinancialMovementId == null), cancellationToken),
            TotalDue = ars.TotalDue, TotalPaid = ars.TotalPaid, TotalBalance = ars.TotalBalance, Months = ars.Months
        };
    }

    private static void Apply(ProjectDeveloperContract entity, ProjectDeveloperContractUpsertRequestDto request)
    {
        entity.DeveloperId = request.DeveloperId; entity.PaymentMode = Enum.Parse<ContractPaymentMode>(request.PaymentMode);
        entity.Percentage = entity.PaymentMode == ContractPaymentMode.Percentage ? request.Percentage : null;
        entity.AgreedAmount = entity.PaymentMode == ContractPaymentMode.FixedAmount ? request.AgreedAmount : null;
        entity.Currency = entity.PaymentMode == ContractPaymentMode.FixedAmount ? request.Currency : null;
        entity.StartDate = request.StartDate; entity.EndDate = request.EndDate; entity.Activo = request.IsActive; entity.Notes = request.Notes?.Trim();
    }
    private static Expression<Func<ProjectDeveloperContract, ProjectDeveloperContractDto>> ToDto() => x => new ProjectDeveloperContractDto
    {
        Id = x.Id, ProjectId = x.ProjectId, ProjectName = x.Project != null ? x.Project.Nombre : "", DeveloperId = x.DeveloperId,
        DeveloperName = x.Developer != null ? x.Developer.FullName : "", PaymentMode = x.PaymentMode.ToString(), Percentage = x.Percentage,
        AgreedAmount = x.AgreedAmount, Currency = x.Currency, StartDate = x.StartDate, EndDate = x.EndDate, IsActive = x.Activo, Notes = x.Notes
    };
    private async Task ValidateAsync(Guid projectId, ProjectDeveloperContractUpsertRequestDto request, Guid? id, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct)) throw new ArgumentException("Proyecto no encontrado");
        if (!await db.Developers.AnyAsync(x => x.Id == request.DeveloperId, ct)) throw new ArgumentException("Miembro no encontrado");
        if (request.PaymentMode is not ("Percentage" or "FixedAmount")) throw new ArgumentException("Modalidad inválida");
        if (request.PaymentMode == "Percentage" && (request.Percentage is null or <= 0 or > 100)) throw new ArgumentException("El porcentaje debe ser mayor a cero y no superar 100");
        if (request.PaymentMode == "Percentage" && Math.Round(request.Percentage!.Value, 2) != request.Percentage) throw new ArgumentException("El porcentaje admite hasta dos decimales");
        if (request.PaymentMode == "FixedAmount") { FinanceRules.Currency(request.Currency); FinanceRules.Money(request.AgreedAmount ?? 0); }
        FinanceRules.Date(request.StartDate); if (request.EndDate.HasValue) FinanceRules.Date(request.EndDate.Value);
        if (request.EndDate < request.StartDate) throw new ArgumentException("El fin no puede ser anterior al inicio");
        if (request.IsActive && await db.ProjectDeveloperContracts.AnyAsync(x => x.Id != id && x.ProjectId == projectId && x.DeveloperId == request.DeveloperId && x.Activo, ct))
            throw new ArgumentException("Ya existe un acuerdo activo para esta persona en el proyecto");
    }
}
