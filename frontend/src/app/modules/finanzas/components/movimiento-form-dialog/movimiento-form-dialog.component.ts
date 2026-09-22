import { Component, Inject, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { FinanzasService } from '../../services/finanzas.service';
import { FinanceOverviewService } from '../../services/finance-overview.service';
import { FINANCE_NATURES, Partner } from '../../models/finance-overview.models';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { compareDates, formatDateToIso, parseIsoDate } from '../../../../core/date.utils';
import { CategoriaFinanciera, EstadoMovimiento, LookupItem, MovimientoDetalle, MovimientoFormulario, TipoMovimiento } from '../../models/finanzas.models';

interface MovimientoFormData {
  tipoInicial: TipoMovimiento;
  movimiento?: MovimientoDetalle;
  categorias: CategoriaFinanciera[];
  clientes: LookupItem[];
  proyectos: LookupItem[];
  proveedores: LookupItem[];
}

@Component({
  selector: 'app-movimiento-form-dialog',
  standalone: true,
  imports: [MatCheckboxModule, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatDatepickerModule, MatInputModule, MatSelectModule],
  templateUrl: './movimiento-form-dialog.component.html',
  styleUrl: './movimiento-form-dialog.component.scss'
})
export class MovimientoFormDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<MovimientoFormDialogComponent>);

  private readonly api = inject(FinanzasService);
  private readonly finance = inject(FinanceOverviewService);
  private readonly requestId = crypto.randomUUID();
  readonly natures = FINANCE_NATURES;
  partners: Partner[] = [];
  saving = false;
  error = '';
  saved?: MovimientoDetalle;
  get estados(): EstadoMovimiento[] { return ['Pendiente', this.form.controls.movementType.value === 'Ingreso' ? 'Cobrado' : 'Pagado', 'Vencido', 'Anulado']; }
  get needsPartner(): boolean { return this.form.controls.nature.value !== 'Operacion'
    || (this.form.controls.movementType.value === 'Egreso' && this.form.controls.funding.value !== 'Empresa'); }
  receiptFile: File | null = null;

  readonly form = this.fb.group({
    movementType: ['Ingreso' as TipoMovimiento, [Validators.required]],
    categoryId: ['', [Validators.required]],
    clientId: [''],
    providerId: [''],
    projectId: [''],
    description: ['', [Validators.required, Validators.maxLength(500)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['ARS', Validators.required],
    nature: ['Operacion', Validators.required],
    funding: ['Empresa', Validators.required],
    partnerId: [''],
    settlementDate: [null as Date | null],
    settlementDateEstimated: [false],
    movementDate: [null as Date | null, [Validators.required]],
    dueDate: [null as Date | null],
    status: ['Pendiente' as EstadoMovimiento, [Validators.required]],
    paymentMethod: ['', [Validators.maxLength(80)]],
    receiptNumber: ['', [Validators.maxLength(80)]],
    notes: ['', [Validators.maxLength(1000)]]
  }, { validators: [movementDateRangeValidator()] });

  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: MovimientoFormData) {
    this.saved = data.movimiento;
    this.finance.partners().subscribe({ next: rows => this.partners = rows, error: () => this.error = 'No se pudieron cargar los socios. Cierra y vuelve a abrir para reintentar.' });
    this.form.patchValue({
      movementType: data.tipoInicial
    });

    if (data.movimiento) {
      this.form.patchValue({
        movementType: data.movimiento.movementType,
        categoryId: data.movimiento.categoryId,
        clientId: data.movimiento.clientId ?? '',
        providerId: data.movimiento.providerId ?? '',
        projectId: data.movimiento.projectId ?? '',
        description: data.movimiento.description,
        amount: data.movimiento.amount,
        currency: data.movimiento.currency, nature: data.movimiento.nature, funding: data.movimiento.funding,
        partnerId: data.movimiento.partnerId ?? '', settlementDate: parseIsoDate(data.movimiento.settlementDate),
        settlementDateEstimated: data.movimiento.settlementDateEstimated,
        movementDate: parseIsoDate(data.movimiento.movementDate),
        dueDate: parseIsoDate(data.movimiento.dueDate),
        status: data.movimiento.status,
        paymentMethod: data.movimiento.paymentMethod ?? '',
        receiptNumber: data.movimiento.receiptNumber ?? '',
        notes: data.movimiento.notes ?? ''
      });
    }
  }

  async guardar(): Promise<void> {
    if (this.saving) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const settled = raw.status === 'Cobrado' || raw.status === 'Pagado';
    if (settled && !raw.settlementDate) { this.error = 'Indica la fecha efectiva del cobro o pago'; return; }
    if (this.needsPartner && !raw.partnerId) { this.error = 'Selecciona el socio'; return; }
    const payload = {
      requestId: this.requestId,
      movementType: raw.movementType,
      categoryId: raw.categoryId,
      clientId: raw.clientId || null,
      providerId: raw.providerId || null,
      projectId: raw.projectId || null,
      description: raw.description,
      amount: Number(raw.amount),
      currency: raw.currency!, nature: raw.nature!, funding: raw.nature === 'Operacion' && raw.movementType === 'Egreso' ? raw.funding! : 'Empresa',
      partnerId: this.needsPartner ? raw.partnerId : null, settlementDate: settled || raw.status === 'Anulado' ? formatDateToIso(raw.settlementDate) : null,
      settlementDateEstimated: !!raw.settlementDateEstimated, expectedVersion: this.saved?.version,
      movementDate: formatDateToIso(raw.movementDate) ?? '',
      dueDate: formatDateToIso(raw.dueDate),
      status: raw.status,
      paymentMethod: raw.paymentMethod || undefined,
      receiptNumber: raw.receiptNumber || undefined,
      notes: raw.notes || undefined,
      receiptFile: this.receiptFile
    } as MovimientoFormulario;
    this.saving = true; this.error = ''; this.dialogRef.disableClose = true; this.form.disable();
    try {
      this.saved = await firstValueFrom(this.saved ? this.api.actualizarMovimiento(this.saved.id, payload) : this.api.crearMovimiento(payload));
      if (this.receiptFile) {
        await firstValueFrom(this.api.subirComprobanteMovimiento(this.saved.id, this.receiptFile)); this.receiptFile = null;
      }
      this.dialogRef.close(true);
    } catch (e: any) { this.error = (this.saved ? 'El registro existente se conserva. ' : '') + (e?.error?.message ?? 'No se pudo completar el guardado; puedes reintentar.'); }
    finally { this.saving = false; this.form.enable(); this.dialogRef.disableClose = !!this.saved; }
  }
  close(): void { if (!this.saving) this.dialogRef.close(!!this.saved); }

  onReceiptSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    if (!file) {
      this.receiptFile = null;
      return;
    }

    if (file.type !== 'application/pdf') {
      this.form.setErrors({ invalidReceiptFile: true });
      this.receiptFile = null;
      input.value = '';
      return;
    }

    this.receiptFile = file;

    if (this.form.hasError('invalidReceiptFile')) {
      const errors = { ...(this.form.errors ?? {}) };
      delete errors['invalidReceiptFile'];
      this.form.setErrors(Object.keys(errors).length ? errors : null);
    }
  }

  categoriasFiltradas(): CategoriaFinanciera[] {
    const type = this.form.value.movementType;
    return this.data.categorias.filter((x) => x.isActive && x.movementType === type);
  }
}

function movementDateRangeValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const movementDate = control.get('movementDate')?.value as Date | null;
    const dueDate = control.get('dueDate')?.value as Date | null;

    if (movementDate && dueDate && compareDates(dueDate, movementDate) < 0) {
      return { invalidDueDate: true };
    }

    return null;
  };
}
