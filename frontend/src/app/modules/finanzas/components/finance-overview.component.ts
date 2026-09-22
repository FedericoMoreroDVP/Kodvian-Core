import { CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, OnInit, OnDestroy, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, firstValueFrom } from 'rxjs';
import { formatDateToIso } from '../../../core/date.utils';
import { FinanceCurrencyCardsComponent } from './finance-currency-cards.component';
import { FinanceOverview, FinanceSetup, Partner } from '../models/finance-overview.models';
import { FinanceOverviewService } from '../services/finance-overview.service';
import { LedgerContrato } from '../../proyectos/models/proyectos.models';

@Component({ selector: 'app-finance-overview', standalone: true,
  imports: [FormsModule, CurrencyPipe, MatButtonModule, FinanceCurrencyCardsComponent],
  templateUrl: './finance-overview.component.html', styleUrl: './finance-overview.component.scss' })
export class FinanceOverviewComponent implements OnInit, OnDestroy {
  private readonly api = inject(FinanceOverviewService);
  private request?: Subscription;
  @Output() changed = new EventEmitter<void>();
  @Output() detail = new EventEmitter<{ currency: string; movementType: 'Ingreso' | 'Egreso'; from: string; to: string }>();
  data?: FinanceOverview;
  setup?: FinanceSetup;
  partners: Partner[] = [];
  partnerName = '';
  period = 'history'; from = ''; to = ''; currency = '';
  loading = false; saving = false; error = ''; message = '';
  settingsOpen = false; partnersOpen = false; exchangeOpen = false;
  exchange = this.newExchange();
  obligations: LedgerContrato[] = []; obligationsYear = new Date().getFullYear(); obligationsPage = 1; obligationsTotal = 0; obligationsLoading = false;
  async loadObligations(page = 1): Promise<void> {
    this.obligationsLoading = true; this.error = '';
    try { const data = await firstValueFrom(this.api.teamObligations(this.obligationsYear, page)); this.obligations = data.items; this.obligationsPage = page; this.obligationsTotal = data.totalCount; }
    catch (e: any) { this.error = e?.error?.message ?? 'No se pudieron consultar los saldos del equipo'; }
    finally { this.obligationsLoading = false; }
  }
  ngOnInit(): void { this.reload(); }
  ngOnDestroy(): void { this.request?.unsubscribe(); }
  get provisional(): boolean { return !this.data?.setup.historyComplete || !!this.data.estimatedDateCount || !!this.data.unclassifiedPayments; }
  get visibleCurrencies() { return this.data?.currencies.filter(x => !this.currency || x.currency === this.currency) ?? []; }
  newExchange() { return { requestId: crypto.randomUUID(), fromCurrency: 'ARS', toCurrency: 'USD', fromAmount: 0, toAmount: 0, date: formatDateToIso(new Date())!, notes: '' }; }
  selectPeriod(): void {
    const today = new Date(); this.from = ''; this.to = '';
    if (this.period === 'month') this.from = formatDateToIso(new Date(today.getFullYear(), today.getMonth(), 1))!;
    if (this.period === 'year') this.from = `${today.getFullYear()}-01-01`;
    if (this.period !== 'custom') this.reload();
  }
  reload(): void {
    if (this.from && this.to && this.from > this.to) { this.error = 'El período es inválido'; return; }
    this.request?.unsubscribe(); this.loading = true; this.error = '';
    this.request = this.api.overview(this.from, this.to).subscribe({
      next: data => { this.data = data; if (!this.settingsOpen) this.setup = { ...data.setup }; this.loading = false; },
      error: e => { this.loading = false; this.error = e?.error?.message ?? 'No se pudo cargar la situación financiera'; }
    });
  }
  openSettings(): void { this.setup = this.data ? { ...this.data.setup } : undefined; this.settingsOpen = !this.settingsOpen; }
  async saveSetup(): Promise<void> {
    if (!this.setup || this.saving) return; this.saving = true; this.error = '';
    try {
      await firstValueFrom(this.api.setup({ ...this.setup, startDate: this.setup.startDate || null }));
      this.settingsOpen = false; this.reload(); this.message = 'Punto de partida guardado';
    } catch (e: any) { this.error = e?.error?.message ?? 'No se pudo guardar'; } finally { this.saving = false; }
  }
  async loadPartners(): Promise<void> {
    this.partnersOpen = true;
    try { this.partners = await firstValueFrom(this.api.partners()); } catch { this.error = 'No se pudieron cargar los socios'; }
  }
  async savePartner(partner?: Partner): Promise<void> {
    if (this.saving) return; this.saving = true; this.error = '';
    try {
      await firstValueFrom(this.api.savePartner(partner?.fullName ?? this.partnerName, partner?.isActive ?? true, partner?.id));
      this.partnerName = ''; await this.loadPartners(); this.reload(); this.changed.emit();
    } catch (e: any) { this.error = e?.error?.message ?? 'No se pudo guardar el socio'; } finally { this.saving = false; }
  }
  async saveExchange(): Promise<void> {
    if (this.saving) return; this.saving = true; this.error = '';
    try {
      await firstValueFrom(this.api.exchange(this.exchange)); this.exchange = this.newExchange(); this.exchangeOpen = false;
      this.reload(); this.changed.emit(); this.message = 'Cambio registrado en ambas monedas. Las comisiones se cargan como gastos separados.';
    } catch (e: any) { this.error = e?.error?.message ?? 'No se pudo registrar el cambio'; } finally { this.saving = false; }
  }
  showDetail(event: { currency: string; movementType: 'Ingreso' | 'Egreso' }): void {
    this.detail.emit({ ...event, from: this.data?.from ?? '', to: this.data?.to ?? '' });
  }
}
