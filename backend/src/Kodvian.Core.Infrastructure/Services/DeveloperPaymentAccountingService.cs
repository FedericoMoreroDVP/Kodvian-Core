using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Application.Finances;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class DeveloperPaymentAccountingService(KodvianDbContext db, ICurrentUser actor)
{
    public async Task<DeveloperPaymentDto> SaveAsync(Guid? id, Guid? contractId, DeveloperPaymentCreateRequestDto request, CancellationToken ct)
    {
        FinanceRules.Currency(request.Currency); FinanceRules.Currency(request.AppliedCurrency);
        FinanceRules.Money(request.Amount); FinanceRules.Money(request.AppliedAmount); FinanceRules.Date(request.PaymentDate);
        if (request.PaymentDate > DateOnly.FromDateTime(DateTime.UtcNow) || request.PeriodYear is < 2000 or > 2100 || request.PeriodMonth is < 1 or > 12)
            throw new ArgumentException("Fecha o período inválido");
        if (request.Currency == request.AppliedCurrency && request.Amount != request.AppliedAmount) throw new ArgumentException("Los importes deben coincidir si la moneda es la misma");
        if (request.Reference?.Length > 120 || request.Notes?.Length > 1000) throw new ArgumentException("Referencia o notas demasiado extensas");
        await using var tx = await FinanceWriteScope.BeginAsync(db, ct);
        var payment = id.HasValue ? await db.DeveloperPayments.Include(x => x.FinancialMovement).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Pago no encontrado") : null;
        if (payment != null && (!payment.Activo || payment.Version != request.ExpectedVersion)) throw new ArgumentException("El pago cambió o está anulado. Actualiza y vuelve a intentar.");
        if (payment == null)
        {
            if (request.RequestId == Guid.Empty) throw new ArgumentException("Identificador de operación requerido");
            var previous = await db.DeveloperPayments.SingleOrDefaultAsync(x => x.RequestId == request.RequestId, ct);
            if (previous != null)
            {
                if (!previous.Activo || previous.ContractId != contractId || previous.Amount != request.Amount || previous.Currency != request.Currency
                    || previous.AppliedAmount != request.AppliedAmount || previous.AppliedCurrency != request.AppliedCurrency || previous.PaymentDate != request.PaymentDate
                    || previous.PeriodYear != request.PeriodYear || previous.PeriodMonth != request.PeriodMonth)
                    throw new ArgumentException("La operación ya se utilizó para otro pago");
                return Map(previous);
            }
        }
        var contract = await db.ProjectDeveloperContracts.Include(x => x.Project).Include(x => x.Developer).SingleOrDefaultAsync(x => x.Id == (payment != null ? payment.ContractId : contractId), ct)
            ?? throw new KeyNotFoundException("Acuerdo no encontrado");
        if (contract.Project?.Estado == ProjectStatus.Cancelado) throw new ArgumentException("No puedes registrar ni modificar pagos en un proyecto cancelado");
        if (payment == null && !contract.Activo) throw new ArgumentException("El acuerdo está inactivo");
        if (contract.PaymentMode == ContractPaymentMode.FixedAmount && contract.Currency != request.AppliedCurrency)
            throw new ArgumentException("Confirma la moneda del acuerdo y utiliza esa moneda para el importe cancelado");
        var historicalReview = payment != null && payment.FinancialMovementId == null;
        if (historicalReview && !request.ExistingMovementId.HasValue) throw new ArgumentException("Vincula este pago histórico con su egreso existente para no duplicarlo");
        if (payment?.FinancialMovementId != null && request.ExistingMovementId.HasValue && request.ExistingMovementId != payment.FinancialMovementId)
            throw new ArgumentException("No se puede cambiar el egreso ya vinculado");
        if (!historicalReview)
        {
            if (await db.DeveloperPayments.AnyAsync(x => x.ContractId == contract.Id && x.Id != id && x.Activo && (x.Currency == null || x.AppliedCurrency == null || x.AppliedAmount == null || x.FinancialMovementId == null), ct))
                throw new ArgumentException("Primero revisa las monedas de los pagos históricos del acuerdo");
            var ledger = (await new ProjectDeveloperContractService(db).GetLedgerAsync(contract.Id, request.PeriodYear, ct))!;
            var currency = ledger.Currencies.Single(x => x.Currency == request.AppliedCurrency);
            var balance = contract.PaymentMode == ContractPaymentMode.FixedAmount ? currency.TotalBalance : currency.Months.Single(x => x.Month == request.PeriodMonth).Balance;
            if (payment != null && payment.AppliedCurrency == request.AppliedCurrency && (contract.PaymentMode == ContractPaymentMode.FixedAmount
                || payment.PeriodYear == request.PeriodYear && payment.PeriodMonth == request.PeriodMonth)) balance += payment.AppliedAmount ?? 0;
            if (request.AppliedAmount > balance) throw new ArgumentException("El importe cancelado supera el saldo del acuerdo para esa moneda y período. Sin ingresos registrados no se genera importe por porcentaje.");
        }

        var movement = payment?.FinancialMovement;
        if (movement == null && request.ExistingMovementId.HasValue)
        {
            movement = await db.FinancialMovements.SingleOrDefaultAsync(x => x.Id == request.ExistingMovementId, ct) ?? throw new KeyNotFoundException("Egreso no encontrado");
            if (await db.DeveloperPayments.AnyAsync(x => x.FinancialMovementId == movement.Id, ct)
                || !movement.Activo || movement.Status is not (FinancialMovementStatus.Pagado or FinancialMovementStatus.Pendiente or FinancialMovementStatus.Vencido) || movement.MovementType != FinancialMovementType.Egreso
                || movement.Nature != "Operacion" || movement.Funding != "Empresa" || movement.ExchangeId != null
                || movement.Amount != request.Amount || movement.Currency != request.Currency || movement.Status == FinancialMovementStatus.Pagado && movement.SettlementDate != request.PaymentDate
                || movement.ProjectId.HasValue && movement.ProjectId != contract.ProjectId)
                throw new ArgumentException("Selecciona un egreso sin vínculo del mismo importe, moneda y proyecto (o sin proyecto). Si ya está pagado, debe coincidir la fecha efectiva.");
            movement.ProjectId = contract.ProjectId;
            movement.Status = FinancialMovementStatus.Pagado;
            movement.SettlementDate = request.PaymentDate;
            movement.SettlementDateEstimated = false;
            movement.FechaActualizacion = DateTime.UtcNow;
            movement.Version = Guid.NewGuid();
        }
        else
        {
            if (movement == null)
            {
                movement = new FinancialMovement { CreatedById = actor.UserId ?? throw new UnauthorizedAccessException(),
                    Category = await FinanceOverviewService.SystemCategoryAsync(db, FinancialMovementType.Egreso, "Pagos al equipo", ct),
                    MovementType = FinancialMovementType.Egreso, Nature = "Operacion", Funding = "Empresa", MovementDate = request.PaymentDate,
                    Description = $"Pago a {contract.Developer?.FullName ?? "miembro del equipo"}" };
                db.FinancialMovements.Add(movement);
            }
            movement.Amount = request.Amount; movement.Currency = request.Currency;
            movement.SettlementDate = request.PaymentDate; movement.SettlementDateEstimated = false; movement.ProjectId = contract.ProjectId;
            movement.Status = FinancialMovementStatus.Pagado;
            movement.ReceiptNumber = request.Reference; movement.Notes = request.Notes; movement.Version = Guid.NewGuid(); movement.FechaActualizacion = DateTime.UtcNow;
        }
        if (payment == null) { payment = new DeveloperPayment { ContractId = contract.Id, RequestId = request.RequestId }; db.DeveloperPayments.Add(payment); }
        payment.FinancialMovement = movement; payment.Amount = request.Amount; payment.Currency = request.Currency;
        payment.AppliedAmount = request.AppliedAmount; payment.AppliedCurrency = request.AppliedCurrency; payment.PaymentDate = request.PaymentDate;
        payment.PeriodYear = request.PeriodYear; payment.PeriodMonth = request.PeriodMonth; payment.Reference = request.Reference?.Trim(); payment.Notes = request.Notes?.Trim();
        payment.Version = Guid.NewGuid(); payment.FechaActualizacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); return Map(payment);
    }
    public async Task CancelAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(db, ct);
        var payment = await db.DeveloperPayments.Include(x => x.FinancialMovement).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Pago no encontrado");
        if (!payment.Activo) return;
        if (payment.Version != expectedVersion) throw new ArgumentException("El pago cambió; actualiza el listado");
        if (payment.FinancialMovement == null) throw new ArgumentException("Vincula primero el egreso histórico para anular ambos registros de forma coherente");
        payment.Activo = false; payment.Version = Guid.NewGuid(); payment.FechaActualizacion = DateTime.UtcNow;
        payment.FinancialMovement.Status = FinancialMovementStatus.Anulado; payment.FinancialMovement.Version = Guid.NewGuid(); payment.FinancialMovement.FechaActualizacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct);
    }
    public static DeveloperPaymentDto Map(DeveloperPayment x) => new()
    {
        Id = x.Id, ContractId = x.ContractId, Amount = x.Amount, Currency = x.Currency, AppliedCurrency = x.AppliedCurrency,
        AppliedAmount = x.AppliedAmount, FinancialMovementId = x.FinancialMovementId, Version = x.Version, IsActive = x.Activo,
        PaymentDate = x.PaymentDate, PeriodYear = x.PeriodYear, PeriodMonth = x.PeriodMonth, Reference = x.Reference, Notes = x.Notes
    };
}
