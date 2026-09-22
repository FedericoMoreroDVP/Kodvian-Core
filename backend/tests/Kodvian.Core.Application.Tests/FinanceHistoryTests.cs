using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Application.Finances.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

public class FinanceHistoryTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly User user = new() { FullName = "Administrador" };
    private readonly Project project = new() { Nombre = "Proyecto", Cliente = new Client { CommercialName = "Cliente" } };
    private readonly FinancialCategory incomeCategory = new() { Name = "Servicios", MovementType = FinancialMovementType.Ingreso };
    private readonly FinancialCategory expenseCategory = new() { Name = "Gastos", MovementType = FinancialMovementType.Egreso };
    private readonly DateOnly date = new(2025, 2, 10);
    private FinanceOverviewService Overview => new(db, new Actor(user));
    private FinancialMovementService Movements => new(db, new NoStorage(), Options.Create(new StorageOptions()), Overview);
    private DeveloperPaymentAccountingService Payments => new(db, new Actor(user));
    public FinanceHistoryTests() { db.AddRange(user, project, incomeCategory, expenseCategory); db.SaveChanges(); }
    private FinancialMovement Add(decimal amount, string currency = "ARS", bool income = true, string nature = "Operacion", string funding = "Empresa", Partner? partner = null,
        FinancialMovementStatus? status = null, DateOnly? settlementDate = null)
    {
        var item = new FinancialMovement { Amount = amount, Currency = currency, MovementType = income ? FinancialMovementType.Ingreso : FinancialMovementType.Egreso,
            Category = income ? incomeCategory : expenseCategory, ProjectId = project.Id, CreatedById = user.Id, Nature = nature, Funding = funding, Partner = partner,
            Status = status ?? (income ? FinancialMovementStatus.Cobrado : FinancialMovementStatus.Pagado), MovementDate = date, SettlementDate = settlementDate ?? date, Description = "Movimiento" };
        if (item.Status is FinancialMovementStatus.Pendiente or FinancialMovementStatus.Vencido) item.SettlementDate = null;
        db.FinancialMovements.Add(item); db.SaveChanges(); return item;
    }
    private ProjectDeveloperContract Contract(bool percentage = false, string? currency = "USD", decimal amount = 300)
    {
        var entity = new ProjectDeveloperContract { ProjectId = project.Id, Developer = new Developer { FullName = "Miembro" },
            PaymentMode = percentage ? ContractPaymentMode.Percentage : ContractPaymentMode.FixedAmount, Percentage = percentage ? 30 : null,
            AgreedAmount = percentage ? null : amount, Currency = percentage ? null : currency, StartDate = new DateOnly(2025, 1, 1) };
        db.ProjectDeveloperContracts.Add(entity); db.SaveChanges(); return entity;
    }
    private DeveloperPaymentCreateRequestDto PaymentRequest(decimal amount = 450000, string currency = "ARS", decimal applied = 300, string appliedCurrency = "USD") =>
        new() { Amount = amount, Currency = currency, AppliedAmount = applied, AppliedCurrency = appliedCurrency, PaymentDate = date, PeriodYear = 2025, PeriodMonth = 2, RequestId = Guid.NewGuid() };

    [Fact]
    public async Task ExistingReceiptsRemainInHistoryWithoutOpeningBalanceAndMonthlyViewStaysMonthly()
    {
        Add(1500000, settlementDate: new DateOnly(2025, 5, 7));
        Add(1000000, settlementDate: new DateOnly(2025, 5, 31));
        Add(1000000, settlementDate: new DateOnly(2025, 6, 14));
        Add(200000, settlementDate: new DateOnly(2025, 7, 6));
        Add(1500000, settlementDate: new DateOnly(2025, 7, 23));
        Add(400000, income: false, settlementDate: new DateOnly(2025, 9, 1));
        var history = await Overview.GetAsync(null, new(2025, 9, 22), default);
        var ars = history.Currencies.Single(x => x.Currency == "ARS");
        Assert.Equal(5200000, ars.Income); Assert.Equal(400000, ars.Expense);
        Assert.Equal(4800000, ars.RecordedCashBalance); Assert.Null(ars.Balance);
        Assert.Equal(4800000, history.Months.Single(x => x.Currency == "ARS" && x.Month == 9).RecordedCashBalance);

        var monthly = await Overview.GetPeriodSummaryAsync(new(2025, 9, 1), new(2025, 9, 30), default);
        Assert.Equal(0, monthly.Currencies.Single(x => x.Currency == "ARS").Income);
        Assert.Equal(-400000, monthly.Currencies.Single(x => x.Currency == "ARS").Result);
    }

    [Fact]
    public async Task OpeningDateDoesNotHideEarlierReceiptsOrCountThemTwice()
    {
        Add(5200000, settlementDate: new DateOnly(2025, 7, 1));
        Add(400000, income: false, settlementDate: new DateOnly(2025, 9, 1));
        await Overview.SetupAsync(new() { StartDate = new(2025, 9, 1), OpeningArs = 5200000, OpeningUsd = 0 }, default);
        var history = await Overview.GetAsync(null, new(2025, 9, 22), default);
        Assert.Null(history.From);
        var ars = history.Currencies.Single(x => x.Currency == "ARS");
        Assert.Equal(5200000, ars.Income);
        Assert.Equal(4800000, ars.RecordedCashBalance);
        Assert.Equal(4800000, ars.Balance);
    }

    [Fact]
    public async Task CompactSummaryMatchesPeriodTotalsButDoesNotExposeHistoricalConfiguration()
    {
        Add(1000); Add(200, income: false); Add(30, "USD");
        Add(999, nature: "AporteSocio"); Add(50, status: FinancialMovementStatus.Vencido);
        var full = await Overview.GetAsync(new(2025, 2, 1), new(2025, 2, 28), default);
        var compact = await Overview.GetPeriodSummaryAsync(new(2025, 2, 1), new(2025, 2, 28), default);
        foreach (var row in compact.Currencies)
        {
            var expected = full.Currencies.Single(x => x.Currency == row.Currency);
            Assert.Equal(expected.Income, row.Income); Assert.Equal(expected.Expense, row.Expense);
            Assert.Equal(expected.Result, row.Result); Assert.Equal(expected.PendingIncome, row.PendingIncome);
        }
        Assert.DoesNotContain("recordedCashBalance", System.Text.Json.JsonSerializer.Serialize(compact, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task EmptyHistoryDoesNotInventZeroOpeningBalances()
    {
        var result = await Overview.GetAsync(null, null, default);
        Assert.False(result.Setup.HistoryComplete);
        Assert.All(result.Currencies, x => { Assert.Null(x.Balance); Assert.Equal(0, x.Result); });
        Assert.Empty(result.Months);
    }

    [Fact]
    public async Task SeparatesCurrenciesCashPendingVoidedAndFutureMovements()
    {
        Add(1000); Add(200, income: false); Add(40, "USD"); Add(10, "USD", false);
        Add(500, status: FinancialMovementStatus.Pendiente); Add(5, "USD", false, status: FinancialMovementStatus.Vencido);
        Add(99999, status: FinancialMovementStatus.Anulado); Add(99999, settlementDate: new DateOnly(2099, 1, 1));
        var result = await Overview.GetAsync(null, new DateOnly(2025, 12, 31), default);
        var ars = result.Currencies.Single(x => x.Currency == "ARS"); var usd = result.Currencies.Single(x => x.Currency == "USD");
        Assert.Equal(800, ars.Result); Assert.Equal(500, ars.PendingIncome); Assert.Equal(30, usd.Result); Assert.Equal(5, usd.PendingExpense);
        Assert.Equal(2, result.Expenses.Count);
    }

    [Fact]
    public async Task UsesEffectiveDateAndRecalculatesHistoricalExpense()
    {
        var movement = Add(1000, settlementDate: new DateOnly(2025, 3, 5));
        movement.SettlementDateEstimated = true; db.SaveChanges();
        var february = await Overview.GetAsync(new(2025, 2, 1), new(2025, 2, 28), default);
        Assert.Equal(0, february.Currencies.Single(x => x.Currency == "ARS").Income);
        var march = await Overview.GetAsync(new(2025, 3, 1), new(2025, 3, 31), default);
        Assert.Equal(1000, march.Currencies.Single(x => x.Currency == "ARS").Income); Assert.Equal(1, march.EstimatedDateCount);
        Add(250, income: false, settlementDate: new DateOnly(2025, 3, 2));
        Assert.Equal(750, (await Overview.GetAsync(new(2025, 3, 1), new(2025, 3, 31), default)).Currencies.Single(x => x.Currency == "ARS").Result);
    }

    [Fact]
    public async Task OpeningBalanceCountsOnlyCashSinceAnchorEvenForCustomPeriod()
    {
        Add(9999, settlementDate: new DateOnly(2025, 1, 1)); Add(200); Add(50, income: false);
        Add(20, settlementDate: new DateOnly(2025, 3, 1));
        await Overview.SetupAsync(new() { StartDate = new(2025, 2, 1), OpeningArs = 500, OpeningUsd = null }, default);
        var result = await Overview.GetAsync(new(2025, 3, 1), new(2025, 3, 31), default);
        Assert.Equal(20, result.Currencies.Single(x => x.Currency == "ARS").Income);
        Assert.Equal(670, result.Currencies.Single(x => x.Currency == "ARS").Balance);
        Assert.Null(result.Currencies.Single(x => x.Currency == "USD").Balance);
        Assert.Equal(670, result.Months.Single(x => x.Currency == "ARS").Balance);
    }

    [Fact]
    public async Task PartnerExpensesDoNotReduceCompanyCashTwice()
    {
        var partner = new Partner { FullName = "Socio" }; db.Partners.Add(partner); db.SaveChanges();
        Add(100, nature: "AporteSocio", partner: partner); Add(20, income: false, nature: "RetiroSocio", partner: partner);
        Add(30, income: false, funding: "SocioAporte", partner: partner); Add(40, income: false, funding: "SocioReintegrable", partner: partner);
        Add(15, income: false, nature: "ReintegroSocio", partner: partner);
        var result = await Overview.GetAsync(null, new(2025, 12, 31), default); var ars = result.Currencies.Single(x => x.Currency == "ARS");
        Assert.Equal(-70, ars.Result); Assert.Equal(130, ars.Contributions); Assert.Equal(65, ars.CashChange);
        Assert.Equal(25, Assert.Single(result.Partners).Outstanding);
    }

    [Fact]
    public async Task ExchangeIsPairedIdempotentExcludedFromProfitAndCanceledTogether()
    {
        var request = new ExchangeRequest { RequestId = Guid.NewGuid(), FromCurrency = "ARS", ToCurrency = "USD", FromAmount = 200, ToAmount = 2, Date = date };
        await Overview.ExchangeAsync(request, default); await Overview.ExchangeAsync(request, default);
        Assert.Equal(2, await db.FinancialMovements.CountAsync());
        var result = await Overview.GetAsync(null, new(2025, 12, 31), default);
        Assert.All(result.Currencies, x => Assert.Equal(0, x.Result));
        Assert.Equal(-200, result.Currencies.Single(x => x.Currency == "ARS").CashChange);
        Assert.Equal(2, result.Currencies.Single(x => x.Currency == "USD").CashChange);
        await Overview.CancelExchangeAsync(request.RequestId, default);
        Assert.All((await Overview.GetAsync(null, null, default)).Currencies, x => Assert.Equal(0, x.CashChange));
        await Assert.ThrowsAsync<ArgumentException>(() => Overview.ExchangeAsync(request, default));
    }

    [Fact]
    public async Task PercentagesUseRegisteredIncomeByCurrencyAndExcludeCapital()
    {
        var contract = Contract(true);
        Add(1000, status: FinancialMovementStatus.Pendiente); Add(500, status: FinancialMovementStatus.Vencido); Add(200, "USD");
        Add(9999, nature: "AporteSocio"); Add(9999, nature: "CambioMoneda"); Add(9999, status: FinancialMovementStatus.Anulado);
        var ledger = (await new ProjectDeveloperContractService(db).GetLedgerAsync(contract.Id, 2025))!;
        Assert.Equal(450, ledger.Currencies.Single(x => x.Currency == "ARS").TotalDue);
        Assert.Equal(60, ledger.Currencies.Single(x => x.Currency == "USD").TotalDue);
        Assert.Equal(0, ledger.Currencies.Single(x => x.Currency == "ARS").Months.Single(x => x.Month == 3).DueAmount);
    }

    [Fact]
    public async Task PercentageWithoutIncomeCannotGeneratePayment()
    {
        var contract = Contract(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Payments.SaveAsync(null, contract.Id, PaymentRequest(), default));
        Assert.Empty(db.DeveloperPayments); Assert.Empty(db.FinancialMovements);
    }

    [Fact]
    public async Task CrossCurrencyPaymentCreatesOneExpenseAndCorrectionsAndCancellationStayLinked()
    {
        var contract = Contract(); var request = PaymentRequest();
        var payment = await Payments.SaveAsync(null, contract.Id, request, default);
        var retry = await Payments.SaveAsync(null, contract.Id, request, default);
        Assert.Equal(payment.Id, retry.Id); Assert.Single(db.FinancialMovements); Assert.Single(db.DeveloperPayments);
        Assert.Equal(1500, payment.ExchangeRate);
        var ledger = (await new ProjectDeveloperContractService(db).GetLedgerAsync(contract.Id, 2025))!;
        Assert.Equal(300, ledger.Currencies.Single(x => x.Currency == "USD").TotalPaid);
        Assert.Equal(0, ledger.Currencies.Single(x => x.Currency == "ARS").TotalPaid);
        var overview = await Overview.GetAsync(null, new(2025, 12, 31), default);
        Assert.Equal(450000, overview.Currencies.Single(x => x.Currency == "ARS").Expense);
        request.Amount = 435000; request.ExpectedVersion = payment.Version; request.PaymentDate = date.AddDays(1);
        var updated = await Payments.SaveAsync(payment.Id, null, request, default);
        Assert.Equal(435000, (await db.FinancialMovements.SingleAsync()).Amount);
        Assert.Equal(date, (await db.FinancialMovements.SingleAsync()).MovementDate);
        Assert.Equal(date.AddDays(1), (await db.FinancialMovements.SingleAsync()).SettlementDate);
        await Assert.ThrowsAsync<ArgumentException>(() => Payments.CancelAsync(updated.Id, payment.Version, default));
        await Payments.CancelAsync(updated.Id, updated.Version, default);
        Assert.Equal(FinancialMovementStatus.Anulado, (await db.FinancialMovements.SingleAsync()).Status);
        Assert.False((await db.DeveloperPayments.SingleAsync()).Activo);
        Assert.Equal(0, (await Overview.GetAsync(null, null, default)).Currencies.Single(x => x.Currency == "ARS").Expense);
    }

    [Fact]
    public async Task PayingAnExistingPendingExpenseSettlesItWithoutDuplicatingTheObligation()
    {
        var contract = Contract(); var expense = Add(450000, income: false, status: FinancialMovementStatus.Vencido);
        expense.MovementDate = new DateOnly(2025, 1, 10); db.SaveChanges();
        var request = PaymentRequest(); request.ExistingMovementId = expense.Id;
        var saved = await Payments.SaveAsync(null, contract.Id, request, default);
        Assert.Equal(expense.Id, saved.FinancialMovementId); Assert.Single(db.FinancialMovements);
        Assert.Equal(FinancialMovementStatus.Pagado, expense.Status); Assert.Equal(date, expense.SettlementDate);
        Assert.Equal(new DateOnly(2025, 1, 10), expense.MovementDate);
        var result = (await Overview.GetAsync(null, null, default)).Currencies.Single(x => x.Currency == "ARS");
        Assert.Equal(0, result.PendingExpense); Assert.Equal(450000, result.Expense);
    }

    [Fact]
    public async Task HistoricalPaymentRequiresExistingExpenseAndDoesNotDuplicateIt()
    {
        var contract = Contract(currency: "ARS", amount: 400);
        var expense = Add(400, income: false);
        var historical = new DeveloperPayment { ContractId = contract.Id, Amount = 400, PaymentDate = date, PeriodMonth = 2, PeriodYear = 2025 };
        db.DeveloperPayments.Add(historical); db.SaveChanges();
        var request = PaymentRequest(400, "ARS", 400, "ARS"); request.ExpectedVersion = historical.Version;
        await Assert.ThrowsAsync<ArgumentException>(() => Payments.SaveAsync(historical.Id, null, request, default));
        request.ExistingMovementId = expense.Id;
        var saved = await Payments.SaveAsync(historical.Id, null, request, default);
        Assert.Equal(expense.Id, saved.FinancialMovementId); Assert.Single(db.FinancialMovements);
        Assert.Equal(0, (await Overview.GetAsync(null, null, default)).UnclassifiedPayments);
        var otherContract = Contract(currency: "ARS", amount: 1000);
        await Assert.ThrowsAsync<ArgumentException>(() => Payments.SaveAsync(null, otherContract.Id, new() { RequestId = Guid.NewGuid(), Amount = 400,
            AppliedAmount = 400, Currency = "ARS", AppliedCurrency = "ARS", PaymentDate = date, PeriodYear = 2025, PeriodMonth = 2, ExistingMovementId = expense.Id }, default));
    }

    [Fact]
    public async Task FixedAgreementCarriesPaymentsAcrossYearsAndUnknownCurrencyIsVisible()
    {
        var contract = Contract(); await Payments.SaveAsync(null, contract.Id, PaymentRequest(), default);
        var ledger = (await new ProjectDeveloperContractService(db).GetLedgerAsync(contract.Id, 2026))!;
        Assert.Equal(0, ledger.Currencies.Single(x => x.Currency == "USD").TotalBalance);
        var unknown = Contract(currency: null);
        Assert.True((await new ProjectDeveloperContractService(db).GetLedgerAsync(unknown.Id, 2025))!.NeedsReview);
        await Assert.ThrowsAsync<ArgumentException>(() => Payments.SaveAsync(null, unknown.Id, PaymentRequest(), default));
    }

    [Fact]
    public async Task RejectsOverReimbursementAndIncompatibleMovementState()
    {
        var partner = new Partner { FullName = "Socio" }; db.Partners.Add(partner); db.SaveChanges();
        Add(40, income: false, funding: "SocioReintegrable", partner: partner);
        var request = new FinancialMovementUpsertRequestDto { RequestId = Guid.NewGuid(), Amount = 50, Currency = "ARS", Nature = "ReintegroSocio", PartnerId = partner.Id,
            MovementType = "Egreso", Status = "Pagado", CategoryId = expenseCategory.Id, MovementDate = date, SettlementDate = date, Description = "Reintegro" };
        await Assert.ThrowsAsync<ArgumentException>(() => Movements.CreateAsync(user.Id, request));
        request.Amount = 20;
        var saved = await Movements.CreateAsync(user.Id, request);
        Assert.Equal(20, (await Overview.GetAsync(null, null, default)).Partners.Single().Outstanding);
        request.ExpectedVersion = saved.Version; request.Status = "Cobrado";
        await Assert.ThrowsAsync<ArgumentException>(() => Movements.UpdateAsync(saved.Id, request));
    }

    [Fact]
    public async Task ManualExpenseRetryDoesNotDuplicateTheMovement()
    {
        var request = new FinancialMovementUpsertRequestDto { RequestId = Guid.NewGuid(), Amount = 20, Currency = "USD", Description = "Gasto", CategoryId = expenseCategory.Id,
            MovementType = "Egreso", Status = "Pagado", MovementDate = date, SettlementDate = date };
        var first = await Movements.CreateAsync(user.Id, request); var retry = await Movements.CreateAsync(user.Id, request);
        Assert.Equal(first.Id, retry.Id); Assert.Single(db.FinancialMovements);
        request.Amount = 25;
        await Assert.ThrowsAsync<ArgumentException>(() => Movements.CreateAsync(user.Id, request));
    }

    [Fact]
    public async Task ManualEditOfLinkedExpenseIsRejectedAndSetupDetectsStaleVersion()
    {
        var payment = await Payments.SaveAsync(null, Contract().Id, PaymentRequest(), default);
        var movement = await db.FinancialMovements.SingleAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Movements.UpdateAsync(movement.Id, new() { Amount = 1, Description = "Cambio", CategoryId = movement.CategoryId,
            MovementType = "Egreso", Status = "Pagado", MovementDate = date, SettlementDate = date, ExpectedVersion = movement.Version }));
        await Overview.SetupAsync(new() { StartDate = date, OpeningArs = 0, OpeningUsd = 0 }, default);
        await Assert.ThrowsAsync<ArgumentException>(() => Overview.SetupAsync(new() { StartDate = date, OpeningArs = 0, OpeningUsd = 0 }, default));
    }

    public void Dispose() => db.Dispose();
    private sealed class Actor(User user) : ICurrentUser { public Guid? UserId => user.Id; public Guid? SessionVersion => user.SessionVersion; }
    private sealed class NoStorage : IFileStorageService
    {
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
