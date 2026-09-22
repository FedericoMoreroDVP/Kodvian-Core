import { CurrencyPipe } from '@angular/common';
import { Component, Inject, OnDestroy, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Subscription, firstValueFrom } from 'rxjs';
import { formatDateToIso, parseIsoDate } from '../../../../core/date.utils';
import { PagoDesarrollador, PagoDesarrolladorFormulario } from '../../models/proyectos.models';
import { ProyectosService } from '../../services/proyectos.service';
import { FinanzasService } from '../../../finanzas/services/finanzas.service';
import { MovimientoListado } from '../../../finanzas/models/finanzas.models';

interface PagoDialogData { contractId: string; currency?: string | null; payment?: PagoDesarrollador; }

@Component({ selector: 'app-pago-desarrollador-form-dialog', standalone: true,
  imports: [CurrencyPipe, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatDatepickerModule, MatInputModule, MatSelectModule],
  templateUrl: './pago-desarrollador-form-dialog.component.html', styleUrl: './pago-desarrollador-form-dialog.component.scss' })
export class PagoDesarrolladorFormDialogComponent implements OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<PagoDesarrolladorFormDialogComponent>);
  private readonly api = inject(ProyectosService);
  private readonly finance = inject(FinanzasService);
  private readonly subscription: Subscription;
  private readonly requestId = crypto.randomUUID();
  saved?: PagoDesarrollador;
  changed = false; saving = false; error = ''; linkExisting = false;
  receiptFile: File | null = null;
  candidates: MovimientoListado[] = []; candidatePage = 1; candidateTotal = 0; loadingCandidates = false;
  readonly form = this.fb.group({
    paymentDate: [new Date(), Validators.required], amount: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['ARS', Validators.required], appliedCurrency: ['ARS', Validators.required], appliedAmount: [0, [Validators.required, Validators.min(0.01)]],
    existingMovementId: [''], periodYear: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    periodMonth: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    reference: ['', Validators.maxLength(120)], notes: ['', Validators.maxLength(1000)]
  });
  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: PagoDialogData) {
    this.saved = data.payment;
    this.form.patchValue({ currency: data.currency || 'ARS', appliedCurrency: data.currency || 'ARS' });
    if (data.payment) {
      const payment = data.payment;
      this.linkExisting = !payment.financialMovementId;
      this.form.patchValue({ paymentDate: parseIsoDate(payment.paymentDate), amount: payment.amount, currency: payment.currency ?? '',
        appliedCurrency: payment.appliedCurrency ?? data.currency ?? '', appliedAmount: payment.appliedAmount ?? 0,
        periodYear: payment.periodYear, periodMonth: payment.periodMonth, reference: payment.reference ?? '', notes: payment.notes ?? '' });
    }
    this.subscription = this.form.valueChanges.subscribe(() => {
      if (this.form.controls.currency.value && this.form.controls.currency.value === this.form.controls.appliedCurrency.value)
        this.form.controls.appliedAmount.setValue(this.form.controls.amount.value, { emitEvent: false });
    });
  }
  ngOnDestroy(): void { this.subscription.unsubscribe(); }
  async loadCandidates(page = 1): Promise<void> {
    this.loadingCandidates = true; this.error = ''; this.candidatePage = page;
    try {
      const result = await firstValueFrom(this.finance.obtenerMovimientos({ pageNumber: page, pageSize: 10, movementType: 'Egreso',
        currency: this.form.controls.currency.value || undefined, exactAmount: this.form.controls.amount.value || undefined, unlinkedOnly: true }));
      this.candidates = result.items; this.candidateTotal = result.totalCount;
    } catch { this.error = 'No se pudieron cargar los egresos existentes'; } finally { this.loadingCandidates = false; }
  }
  async guardar(): Promise<void> {
    if (this.saving) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const raw = this.form.getRawValue();
    if (!this.saved?.financialMovementId && this.linkExisting && !raw.existingMovementId) { this.error = 'Selecciona el egreso existente'; return; }
    const payload: PagoDesarrolladorFormulario = {
      paymentDate: formatDateToIso(raw.paymentDate) ?? '', amount: Number(raw.amount), currency: raw.currency!, appliedCurrency: raw.appliedCurrency!,
      appliedAmount: Number(raw.appliedAmount), existingMovementId: this.saved?.financialMovementId ?? (this.linkExisting ? raw.existingMovementId : null),
      periodYear: Number(raw.periodYear), periodMonth: Number(raw.periodMonth), reference: raw.reference || undefined, notes: raw.notes || undefined,
      requestId: this.requestId, expectedVersion: this.saved?.version
    };
    this.saving = true; this.error = ''; this.form.disable(); this.dialogRef.disableClose = true;
    try {
      this.saved = await firstValueFrom(this.saved ? this.api.actualizarPago(this.saved.id, payload) : this.api.registrarPagoContrato(this.data.contractId, payload));
      this.changed = true;
      if (this.receiptFile) { await firstValueFrom(this.api.subirComprobantePago(this.saved.id, this.receiptFile)); this.receiptFile = null; }
      this.dialogRef.close(true);
    } catch (e: any) { this.error = (this.changed ? 'El pago y su egreso ya están guardados. ' : '') + (e?.error?.message ?? 'No se pudo completar la operación. Puedes reintentar.'); }
    finally { this.saving = false; this.form.enable(); this.dialogRef.disableClose = this.changed; }
  }
  close(): void { if (!this.saving) this.dialogRef.close(this.changed); }
  onReceiptSelected(event: Event): void {
    const input = event.target as HTMLInputElement; const file = input.files?.[0];
    this.receiptFile = file?.type === 'application/pdf' ? file : null;
    if (file && !this.receiptFile) { input.value = ''; this.error = 'Solo se admite un comprobante PDF'; }
  }
}
