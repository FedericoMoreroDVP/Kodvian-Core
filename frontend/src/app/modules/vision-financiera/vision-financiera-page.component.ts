import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import { FinanceOverviewService } from '../finanzas/services/finance-overview.service';
import { FinanceOverview, Partner, PartnerBalance } from '../finanzas/models/finance-overview.models';
import { LedgerContrato } from '../proyectos/models/proyectos.models';
import { ContratoLedgerDialogComponent } from '../proyectos/components/contrato-ledger-dialog/contrato-ledger-dialog.component';
import { FinanceChartComponent } from './finance-chart.component';
import { MovementDetailDialogComponent, MovementDetailData } from './movement-detail-dialog.component';
import { VisionSettingsData, VisionSettingsDialogComponent } from './vision-settings-dialog.component';
import { ChartSelection, FinanceChartModel, VisionBucket, VisionPeriod, VisionSection, VisionState, dateLabel, money, monthlyBuckets, parseVisionState, periodRange, validDate } from './vision.models';

@Component({ selector: 'app-vision-financiera-page', standalone: true,
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, FinanceChartComponent],
  templateUrl: './vision-financiera-page.component.html', styleUrl: './vision-financiera-page.component.scss' })
export class VisionFinancieraPageComponent implements OnInit {
  private readonly api = inject(FinanceOverviewService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  private obligationsRequest?: Subscription;
  private partnersRequest?: Subscription;
  private rangeKey = '';
  readonly money = money;
  readonly dateLabel = dateLabel;
  readonly sections: { value: VisionSection; label: string; icon: string }[] = [
    { value: 'resumen', label: 'Resumen', icon: 'insights' }, { value: 'socios', label: 'Socios', icon: 'groups' }, { value: 'compromisos', label: 'Compromisos', icon: 'event_note' }
  ];
  state: VisionState = { section: 'resumen', period: 'historico', currency: 'ARS', from: '', to: '' };
  range = { from: '', to: '' };
  draftPeriod: VisionPeriod = 'historico'; draftFrom = ''; draftTo = ''; filterError = '';
  data?: FinanceOverview;
  loading = false; error = ''; loadedAt = '';
  grouping = 'Mensual'; buckets: VisionBucket[] = [];
  expenses: FinanceOverview['expenses'] = []; showAllCategories = false;
  flowChart!: FinanceChartModel; expenseChart!: FinanceChartModel; balanceChart!: FinanceChartModel;
  partners: Partner[] = []; partnersLoaded = false; partnersLoading = false; partnersError = '';
  obligations: LedgerContrato[] = []; obligationsYear = new Date().getFullYear(); obligationsPage = 1; obligationsTotal = 0;
  obligationsLoading = false; obligationsLoaded = false; obligationsError = '';

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(query => {
      this.state = parseVisionState(query);
      this.draftPeriod = this.state.period; this.draftFrom = this.state.from; this.draftTo = this.state.to;
      this.range = periodRange(this.state);
      this.filterError = '';
      if (this.state.period === 'personalizado' && (!validDate(this.range.from) || !validDate(this.range.to) || this.range.from > this.range.to)) {
        this.request?.unsubscribe(); this.data = undefined; this.loading = false;
        this.error = 'Elige fechas válidas y ordenadas entre 2000 y 2100.'; return;
      }
      const key = `${this.range.from}|${this.range.to}`;
      if (key !== this.rangeKey || !this.data && !this.loading) this.reload();
      else if (this.data) this.buildCharts();
      if (this.state.section === 'socios' && !this.partnersLoaded && !this.partnersLoading) this.loadPartners();
      if (this.state.section === 'compromisos' && !this.obligationsLoaded && !this.obligationsLoading) this.loadObligations();
    });
  }
  get row() { return this.data?.currencies.find(x => x.currency === this.state.currency); }
  get provisional(): boolean { return !!this.data && (!this.data.setup.historyComplete || this.data.estimatedDateCount > 0 || this.data.unclassifiedPayments > 0); }
  get periodLabel(): string {
    return { historico: 'Todo el histórico registrado', mes: 'Este mes', anio: 'Este año', personalizado: `${dateLabel(this.state.from)} al ${dateLabel(this.state.to)}` }[this.state.period];
  }
  get narrative(): string {
    const row = this.row;
    if (!row || !row.income && !row.expense) return 'Todavía no hay cobros ni gastos operativos para esta selección.';
    if (row.result > 0) return `Los cobros registrados superan los gastos en ${money(row.result, this.state.currency)}.`;
    if (row.result < 0) return `Los gastos registrados superan los cobros en ${money(-row.result, this.state.currency)}.`;
    return 'Los cobros y gastos registrados del período están equilibrados.';
  }
  get visibleExpenses() { return this.showAllCategories ? this.expenses : this.expenses.slice(0, 5); }
  get partnerCards(): PartnerBalance[] {
    const rows = this.data?.partners.filter(p => p.currency === this.state.currency) ?? [];
    const map = new Map(rows.map(p => [p.partnerId, p]));
    for (const partner of this.partners.filter(p => p.isActive))
      if (!map.has(partner.id)) map.set(partner.id, { partnerId: partner.id, partnerName: partner.fullName, currency: this.state.currency,
        contributions: 0, withdrawals: 0, reimbursableExpenses: 0, reimbursements: 0, outstanding: 0 });
    return [...map.values()].sort((a, b) => a.partnerName.localeCompare(b.partnerName, 'es'));
  }
  get partnerContributions(): number { return this.partnerCards.reduce((sum, p) => sum + p.contributions, 0); }
  get partnerWithdrawals(): number { return this.partnerCards.reduce((sum, p) => sum + p.withdrawals, 0); }
  get partnerOutstanding(): number { return this.partnerCards.reduce((sum, p) => sum + p.outstanding, 0); }
  private navigate(patch: Partial<VisionState>): void {
    const next = { ...this.state, ...patch };
    void this.router.navigate([], { relativeTo: this.route, queryParams: {
      periodo: next.period, moneda: next.currency, seccion: next.section,
      desde: next.period === 'personalizado' ? next.from : null, hasta: next.period === 'personalizado' ? next.to : null
    } });
  }
  selectCurrency(currency: 'ARS' | 'USD'): void { this.navigate({ currency }); }
  selectSection(section: VisionSection): void { this.navigate({ section }); }
  selectPeriod(): void { if (this.draftPeriod !== 'personalizado') this.navigate({ period: this.draftPeriod, from: '', to: '' }); }
  applyCustom(): void {
    if (!validDate(this.draftFrom) || !validDate(this.draftTo) || this.draftFrom > this.draftTo) { this.filterError = 'Indica un período válido entre 2000 y 2100.'; return; }
    this.navigate({ period: 'personalizado', from: this.draftFrom, to: this.draftTo });
  }
  reload(): void {
    if (this.state.period === 'personalizado' && (!validDate(this.range.from) || !validDate(this.range.to) || this.range.from > this.range.to)) {
      this.error = 'Elige fechas válidas y ordenadas entre 2000 y 2100.'; return;
    }
    this.request?.unsubscribe(); this.rangeKey = `${this.range.from}|${this.range.to}`;
    this.loading = true; this.error = ''; this.data = undefined;
    this.request = this.api.overview(this.range.from, this.range.to).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => { this.data = data; this.buildCharts(); this.loading = false; this.loadedAt = new Intl.DateTimeFormat('es-AR', { hour: '2-digit', minute: '2-digit' }).format(new Date()); },
      error: e => { this.loading = false; this.error = e?.error?.message ?? 'No se pudo cargar la visión financiera.'; }
    });
  }
  private buildCharts(): void {
    if (!this.data) return;
    const grouped = monthlyBuckets(this.data.months, this.state.currency); this.grouping = grouped.grouping; this.buckets = grouped.buckets;
    this.expenses = this.data.expenses.filter(x => x.currency === this.state.currency).sort((a, b) => b.amount - a.amount || a.category.localeCompare(b.category));
    this.flowChart = { type: 'bar', labels: this.buckets.map(x => x.label), currency: this.state.currency,
      description: `Cobros y gastos por período en ${this.state.currency}. Consulta los datos o selecciona una barra para ver movimientos.`,
      series: [{ label: 'Cobrado', color: '#4dd3ac', values: this.buckets.map(x => x.income) }, { label: 'Gastado', color: '#f2aa68', values: this.buckets.map(x => x.expense) }] };
    const categories = this.expenses.slice(0, 5);
    this.expenseChart = { type: 'bar', horizontal: true, labels: categories.map(x => x.category.length > 24 ? x.category.slice(0, 23) + '…' : x.category), fullLabels: categories.map(x => x.category),
      currency: this.state.currency, description: 'Las cinco principales categorías de gastos pagados. Consulta los datos para acceder a todas.',
      series: [{ label: 'Gastado', color: '#f2aa68', values: categories.map(x => x.amount) }] };
    this.balanceChart = { type: 'line', labels: this.buckets.map(x => x.label), currency: this.state.currency,
      description: `Evolución del neto registrado en ${this.state.currency}, acumulado al cierre de cada período. No equivale a saldo bancario confirmado.`,
      series: [{ label: 'Neto registrado', color: '#81a9ff', values: this.buckets.map(x => x.balance) }] };
  }
  openDetail(title: string, amount: number, view: string, options: { categoryId?: string; partnerId?: string; from?: string; to?: string; cumulative?: boolean; subtitle?: string } = {}): void {
    if (!this.data) return;
    const pending = view.startsWith('Pending');
    const filters: MovementDetailData['filters'] = { view, categoryId: options.categoryId, partnerId: options.partnerId,
      dateFrom: options.cumulative ? undefined : options.from ?? (this.range.from || undefined),
      dateTo: options.to ?? (pending ? this.range.to || undefined : this.data.to) };
    const data: MovementDetailData = { title, amount, currency: this.state.currency, filters,
      subtitle: options.subtitle ?? (options.cumulative ? `Acumulado hasta ${dateLabel(filters.dateTo)}` : this.periodLabel) };
    this.dialog.open(MovementDetailDialogComponent, { width: '1100px', maxWidth: '96vw', maxHeight: '92vh', data, autoFocus: 'dialog' });
  }
  openMonth(bucket: VisionBucket, kind: 'income' | 'expense' | 'result' | 'balance'): void {
    if (!this.data) return;
    const from = this.range.from && this.range.from > bucket.from ? this.range.from : bucket.from;
    const to = this.data.to < bucket.to ? this.data.to : bucket.to;
    const views = { income: 'OperationalIncome', expense: 'OperationalExpense', result: 'OperationalResult', balance: 'RecordedCash' };
    const titles = { income: 'Cobros', expense: 'Gastos', result: 'Resultado operativo', balance: 'Neto registrado' };
    this.openDetail(`${titles[kind]} · ${bucket.label}`, bucket[kind], views[kind], { from, to, cumulative: kind === 'balance', subtitle: kind === 'balance' ? `Acumulado hasta ${dateLabel(to)}` : `${dateLabel(from)} al ${dateLabel(to)}` });
  }
  flowSelected(event: ChartSelection): void { const bucket = this.buckets[event.index]; if (bucket) this.openMonth(bucket, event.datasetIndex === 0 ? 'income' : 'expense'); }
  balanceSelected(event: ChartSelection): void { const bucket = this.buckets[event.index]; if (bucket) this.openMonth(bucket, 'balance'); }
  expenseSelected(event: ChartSelection): void { const category = this.expenses[event.index]; if (category) this.openCategory(category); }
  openCategory(category: FinanceOverview['expenses'][number]): void { this.openDetail(`Gastos · ${category.category}`, category.amount, 'OperationalExpense', { categoryId: category.categoryId }); }
  share(amount: number): string { return this.row?.expense ? new Intl.NumberFormat('es-AR', { style: 'percent', maximumFractionDigits: 1 }).format(amount / this.row.expense) : '0 %'; }
  configure(mode: VisionSettingsData['mode']): void {
    if (!this.data) return;
    this.dialog.open(VisionSettingsDialogComponent, { width: '680px', maxWidth: '96vw', maxHeight: '92vh', data: { mode, overview: this.data }, autoFocus: 'dialog' })
      .afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
        if (mode !== 'review') { this.reload(); this.partnersLoaded = false; if (this.state.section === 'socios') this.loadPartners(); this.obligationsLoaded = false; }
      });
  }
  loadPartners(): void {
    this.partnersRequest?.unsubscribe();
    this.partnersLoading = true; this.partnersError = '';
    this.partnersRequest = this.api.partners().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: data => { this.partners = data; this.partnersLoaded = true; this.partnersLoading = false; },
      error: () => { this.partnersError = 'No se pudieron cargar los socios'; this.partnersLoading = false; } });
  }
  partnerDetail(partner: PartnerBalance, key: 'contributions' | 'withdrawals' | 'reimbursableExpenses' | 'reimbursements' | 'outstanding'): void {
    const names = { contributions: 'Aportes', withdrawals: 'Retiros', reimbursableExpenses: 'Gastos afrontados a reintegrar', reimbursements: 'Reintegrado', outstanding: 'Pendiente de reintegro' };
    const views = { contributions: 'PartnerContributions', withdrawals: 'PartnerWithdrawals', reimbursableExpenses: 'PartnerReimbursableExpenses', reimbursements: 'PartnerReimbursements', outstanding: 'PartnerOutstanding' };
    this.openDetail(`${names[key]} · ${partner.partnerName}`, partner[key], views[key], { partnerId: partner.partnerId, cumulative: true });
  }
  loadObligations(page = 1): void {
    if (!Number.isInteger(this.obligationsYear) || this.obligationsYear < 2000 || this.obligationsYear > 2100) { this.obligationsError = 'Indica un año entre 2000 y 2100'; return; }
    this.obligationsRequest?.unsubscribe(); this.obligationsLoading = true; this.obligationsError = '';
    this.obligations = []; this.obligationsLoaded = false; this.obligationsTotal = 0;
    this.obligationsRequest = this.api.teamObligations(this.obligationsYear, page).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => { this.obligations = data.items; this.obligationsPage = page; this.obligationsTotal = data.totalCount; this.obligationsLoaded = true; this.obligationsLoading = false; },
      error: () => { this.obligationsError = 'No se pudieron consultar los acuerdos'; this.obligationsLoading = false; }
    });
  }
  obligationRow(contract: LedgerContrato) { return contract.currencies.find(x => x.currency === this.state.currency); }
  applicable(contract: LedgerContrato): boolean { return contract.paymentMode !== 'FixedAmount' || !contract.currency || contract.currency === this.state.currency; }
  openContract(contract: LedgerContrato): void { this.dialog.open(ContratoLedgerDialogComponent, { width: '1050px', maxWidth: '96vw', maxHeight: '92vh', data: contract }); }
}
