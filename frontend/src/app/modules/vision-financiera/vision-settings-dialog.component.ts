import { Component, ElementRef, Inject, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { FinanceOverviewService } from '../finanzas/services/finance-overview.service';
import { FinanceOverview, FinanceSetup, Partner, PartnerPerson } from '../finanzas/models/finance-overview.models';
import { isoDate, money } from './vision.models';

export interface VisionSettingsData { mode: 'setup' | 'partners' | 'exchange' | 'review'; overview: FinanceOverview; }
@Component({ selector: 'app-vision-settings-dialog', standalone: true, imports: [FormsModule, MatDialogModule, MatButtonModule],
  templateUrl: './vision-settings-dialog.component.html',
  styles: [`
    p { color:var(--kd-text-secondary);font-size:14px;line-height:1.6 } label { display:flex;flex-direction:column;gap:8px;font-size:13px; }
    input,select { box-sizing:border-box;min-width:0;width:100%;background:#0d1721;color:#edf3fa;border:1px solid #354657;border-radius:9px;padding:11px;font:inherit; }
    input:focus-visible,select:focus-visible { outline:2px solid #46b99a;outline-offset:2px; }
    .grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin:18px 0}.wide{grid-column:1/-1}.check{flex-direction:row;align-items:center}.check input{width:auto}
    .partner{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:center;padding:16px 0;border-bottom:1px solid #293746}.partner-data{display:grid;gap:8px}.partner-actions{display:flex;flex-direction:column;gap:8px}.mode-buttons,.people-search,.people-pages{display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin:14px 0}.people-search input{flex:1;min-width:180px}.selected-mode{background:#24463c!important;color:#c5f5e6!important}.linked-note{font-size:12px;color:var(--kd-text-secondary)}.person-preview{padding:12px;border:1px solid #354657;border-radius:8px;margin:12px 0}.person-preview strong,.person-preview small{display:block}.people-pages{justify-content:space-between;font-size:12px}
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
  partnerEmail = '';
  partnerMode: 'existing' | 'new' = 'existing';
  people: PartnerPerson[] = [];
  peopleSearch = '';
  peoplePage = 1;
  peopleTotal = 0;
  peopleLoading = false;
  peopleError = '';
  selectedPersonKey = '';
  linkingPartner?: Partner;
  private peopleRevision = 0;
  @ViewChild('partnerEditor') private partnerEditor?: ElementRef<HTMLElement>;
  loading = false; saving = false; error = ''; changed = false;
  exchange = { requestId: crypto.randomUUID(), fromCurrency: 'ARS', toCurrency: 'USD', fromAmount: 0, toAmount: 0, date: isoDate(new Date()), notes: '' };
  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: VisionSettingsData) { this.setup = { ...data.overview.setup }; }
  get title(): string { return { setup: 'Punto de partida', partners: 'Gestionar socios', exchange: 'Registrar cambio de moneda', review: 'Revisión del historial' }[this.data.mode]; }
  ngOnInit(): void { if (this.data.mode === 'partners') { void this.loadPartners(); void this.loadPeople(); } }
  personKey(person: PartnerPerson): string { return `${person.source}:${person.personId}`; }
  get selectedPerson(): PartnerPerson | undefined { return this.people.find(p => this.personKey(p) === this.selectedPersonKey); }
  get similarManualPartners(): Partner[] {
    const person = this.selectedPerson;
    if (!person || this.linkingPartner) return [];
    const normalize = (value?: string | null) => (value ?? '').trim().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es');
    return this.partners.filter(partner => !this.isLinked(partner) && (
      normalize(partner.fullName) === normalize(person.fullName)
      || !!person.email && normalize(partner.email) === normalize(person.email)));
  }
  get canAddPartner(): boolean {
    if (this.saving || this.loading) return false;
    if (this.partnerMode === 'new') return !!this.partnerName.trim();
    return !this.peopleLoading && !!this.selectedPerson && (!this.selectedPerson.registeredPartnerId || this.selectedPerson.registeredPartnerId === this.linkingPartner?.id);
  }
  isLinked(partner: Partner): boolean { return !!partner.personId; }
  selectPartnerMode(mode: 'existing' | 'new'): void {
    if (this.saving) return;
    this.partnerMode = mode; this.linkingPartner = undefined; this.error = ''; this.selectedPersonKey = '';
    if (mode === 'existing') void this.loadPeople();
  }
  linkExistingPartner(partner: Partner): void {
    this.partnerMode = 'existing'; this.linkingPartner = partner; this.peopleSearch = ''; this.error = '';
    void this.loadPeople();
    this.partnerEditor?.nativeElement.scrollIntoView({ block: 'start' });
    this.partnerEditor?.nativeElement.focus({ preventScroll: true });
  }
  async loadPeople(page = 1): Promise<void> {
    const revision = ++this.peopleRevision;
    this.peopleLoading = true; this.peopleError = ''; this.selectedPersonKey = '';
    try {
      const result = await firstValueFrom(this.api.partnerPeople(this.peopleSearch, page));
      if (revision !== this.peopleRevision) return;
      this.people = result.items; this.peoplePage = page; this.peopleTotal = result.totalCount;
    } catch { if (revision === this.peopleRevision) { this.people = []; this.peopleError = 'No se pudieron cargar las personas. Puedes reintentar.'; } }
    finally { if (revision === this.peopleRevision) this.peopleLoading = false; }
  }
  private busy(value: boolean): void { this.saving = value; this.ref.disableClose = value; }
  async loadPartners(): Promise<void> {
    this.loading = true; this.error = '';
    try { this.partners = await firstValueFrom(this.api.partners()); }
    catch { this.error = 'No se pudieron cargar los socios'; }
    finally { this.loading = false; }
  }
  async savePartner(partner?: Partner): Promise<void> {
    if (!partner && !this.canAddPartner) { this.error = this.partnerMode === 'existing' ? 'Selecciona una persona disponible' : 'Indica el nombre de la persona'; return; }
    if (this.saving) return; this.busy(true); this.error = '';
    try {
      if (partner) {
        await firstValueFrom(this.api.savePartner(partner.fullName, partner.isActive, partner.id,
          { source: partner.source ?? 'Manual', personId: partner.personId ?? null, email: partner.email ?? '' }));
      } else if (this.partnerMode === 'existing') {
        const person = this.selectedPerson!;
        await firstValueFrom(this.api.savePartner('', this.linkingPartner?.isActive ?? true, this.linkingPartner?.id,
          { source: person.source, personId: person.personId }));
      } else {
        await firstValueFrom(this.api.savePartner(this.partnerName, true, undefined, { source: 'Manual', email: this.partnerEmail }));
      }
      this.changed = true; this.partnerName = ''; this.partnerEmail = ''; this.selectedPersonKey = ''; this.linkingPartner = undefined;
      await this.loadPartners();
      if (this.partnerMode === 'existing') await this.loadPeople(this.peoplePage);
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
