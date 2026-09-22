import { Component, Inject, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { FinanceOverviewService } from '../finanzas/services/finance-overview.service';
import { FinanceOverview, FinanceSetup, Partner } from '../finanzas/models/finance-overview.models';
import { isoDate, money } from './vision.models';

export interface VisionSettingsData { mode: 'setup' | 'partners' | 'exchange' | 'review'; overview: FinanceOverview; }
@Component({ selector: 'app-vision-settings-dialog', standalone: true, imports: [FormsModule, MatDialogModule, MatButtonModule],
  templateUrl: './vision-settings-dialog.component.html',
  styles: [`
    p { color:var(--kd-text-secondary);font-size:14px;line-height:1.6 } label { display:flex;flex-direction:column;gap:8px;font-size:13px; }
    input,select { box-sizing:border-box;min-width:0;width:100%;background:#0d1721;color:#edf3fa;border:1px solid #354657;border-radius:9px;padding:11px;font:inherit; }
    input:focus-visible,select:focus-visible { outline:2px solid #46b99a;outline-offset:2px; }
    .grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin:18px 0}.wide{grid-column:1/-1}.check{flex-direction:row;align-items:center}.check input{width:auto}
    .partner{display:grid;grid-template-columns:1fr auto auto;gap:12px;align-items:center;padding:12px 0;border-bottom:1px solid #293746}
    .error{color:#ffb0a1}details{margin:14px 0}summary{cursor:pointer;font-weight:600}strong{color:#eef5fc}li{margin:12px 0;line-height:1.5}
    @media(max-width:600px){.grid{grid-template-columns:1fr}.partner{grid-template-columns:1fr}.wide{grid-column:auto}}
  `]
})
export class VisionSettingsDialogComponent implements OnInit {
  private readonly api = inject(FinanceOverviewService);
  private readonly ref = inject(MatDialogRef<VisionSettingsDialogComponent>);
  readonly money = money;
  setup: FinanceSetup;
  partners: Partner[] = [];
  partnerName = '';
  loading = false; saving = false; error = ''; changed = false;
  exchange = { requestId: crypto.randomUUID(), fromCurrency: 'ARS', toCurrency: 'USD', fromAmount: 0, toAmount: 0, date: isoDate(new Date()), notes: '' };
  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: VisionSettingsData) { this.setup = { ...data.overview.setup }; }
  get title(): string { return { setup: 'Punto de partida', partners: 'Gestionar socios', exchange: 'Registrar cambio de moneda', review: 'Revisión del historial' }[this.data.mode]; }
  ngOnInit(): void { if (this.data.mode === 'partners') void this.loadPartners(); }
  private busy(value: boolean): void { this.saving = value; this.ref.disableClose = value; }
  async loadPartners(): Promise<void> {
    this.loading = true; this.error = '';
    try { this.partners = await firstValueFrom(this.api.partners()); }
    catch { this.error = 'No se pudieron cargar los socios'; }
    finally { this.loading = false; }
  }
  async savePartner(partner?: Partner): Promise<void> {
    if (this.saving) return; this.busy(true); this.error = '';
    try {
      await firstValueFrom(this.api.savePartner(partner?.fullName ?? this.partnerName, partner?.isActive ?? true, partner?.id));
      this.changed = true; this.partnerName = ''; await this.loadPartners();
    } catch (e: any) { this.error = e?.error?.message ?? 'No se pudo guardar el socio'; }
    finally { this.busy(false); }
  }
  async save(): Promise<void> {
    if (this.saving) return; this.busy(true); this.error = '';
    try {
      if (this.data.mode === 'setup') await firstValueFrom(this.api.setup({ ...this.setup, startDate: this.setup.startDate || null }));
      if (this.data.mode === 'exchange') await firstValueFrom(this.api.exchange(this.exchange));
      this.changed = true; this.ref.close(true);
    } catch (e: any) { this.error = e?.error?.message ?? 'No se pudo guardar. Lo ingresado se conservó para reintentar.'; }
    finally { this.busy(false); }
  }
  close(): void { if (!this.saving) this.ref.close(this.changed); }
}
