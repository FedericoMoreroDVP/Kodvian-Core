import { Component, ElementRef, Inject, inject } from '@angular/core';
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef, { optional: true });
  get natures() {
    return FINANCE_NATURES.filter(x => x.value === 'Operacion' || (this.form.controls.movementType.value === 'Ingreso'
      ? x.value === 'AporteSocio' : x.value === 'RetiroSocio' || x.value === 'ReintegroSocio'));
  }
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

  onMovementTypeChange(): void {
    const controls = this.form.controls;
    if (!this.natures.some(x => x.value === controls.nature.value)) controls.nature.setValue('Operacion');
    if (controls.movementType.value === 'Ingreso') controls.funding.setValue('Empresa');
    if (controls.status.value === 'Pagado' && controls.movementType.value === 'Ingreso') controls.status.setValue('Cobrado');
    else if (controls.status.value === 'Cobrado' && controls.movementType.value === 'Egreso') controls.status.setValue('Pagado');
    if (!this.categoriasFiltradas().some(x => x.id === controls.categoryId.value)) controls.categoryId.setValue('');
    this.onClassificationChange();
  }

  onClassificationChange(): void {
    if (this.form.controls.nature.value !== 'Operacion') this.form.controls.funding.setValue('Empresa');
    if (!this.needsPartner) this.form.controls.partnerId.setValue('');
    this.error = '';
  }

  private showIssue(message: string, control?: keyof typeof this.form.controls): void {
    this.error = message;
    if (control) {
      this.form.controls[control].markAsTouched();
      const element = this.host?.nativeElement.querySelector<HTMLElement>(`[formcontrolname="${control}"]`);
      element?.focus({ preventScroll: true });
      element?.scrollIntoView({ block: 'nearest' });
    }
  }

  private showFormErrors(): void {
    this.form.markAllAsTouched();
    if (this.form.hasError('invalidDueDate')) { this.showIssue('La fecha de vencimiento no puede ser anterior a la fecha de movimiento.', 'dueDate'); return; }
    if (this.form.hasError('invalidReceiptFile')) { this.showIssue('El comprobante debe ser un archivo PDF.'); return; }
    const fields = { movementType: 'Tipo de movimiento', categoryId: 'Categoría', description: 'Descripción', amount: 'Monto',
      currency: 'Moneda', nature: 'Clasificación', funding: 'Quién afrontó el gasto', settlementDate: 'Fecha efectiva',
      movementDate: 'Fecha de movimiento', dueDate: 'Fecha de vencimiento', status: 'Estado', paymentMethod: 'Medio de pago',
      receiptNumber: 'Número de comprobante', notes: 'Observaciones' };
    for (const name of Object.keys(fields) as (keyof typeof fields)[]) {
      const errors = this.form.controls[name].errors;
      if (!errors) continue;
      const reason = errors['required'] ? 'completa este campo' : errors['maxlength'] ? `máximo ${errors['maxlength'].requiredLength} caracteres` : 'revisa el valor ingresado';
      this.showIssue(`${fields[name]}: ${reason}.`, name); return;
    }
    this.showIssue('Revisa los campos del movimiento antes de guardar.');
  }

  async guardar(): Promise<void> {
    if (this.saving) return;
    if (this.form.invalid) {
      this.showFormErrors();
      return;
    }

    const raw = this.form.getRawValue();
    if (!this.natures.some(x => x.value === raw.nature)) {
      this.showIssue(raw.movementType === 'Egreso'
        ? 'Para un gasto pagado por un socio, selecciona Operación de la empresa y luego un socio como aporte en Quién afrontó el gasto.'
        : 'Un ingreso admite Operación de la empresa o Aporte de dinero de un socio.', 'nature'); return;
    }
    if (!this.estados.includes(raw.status!)) { this.showIssue('Selecciona un estado compatible con el tipo de movimiento.', 'status'); return; }
    if (!this.categoriasFiltradas().some(x => x.id === raw.categoryId)) { this.showIssue('Selecciona una categoría compatible con el tipo de movimiento.', 'categoryId'); return; }
    const settled = raw.status === 'Cobrado' || raw.status === 'Pagado';
    if (this.needsPartner && !settled && raw.status !== 'Anulado') {
      this.showIssue(raw.movementType === 'Ingreso' ? 'Un aporte de dinero debe estar Cobrado para registrarse.' : 'Un gasto afrontado por un socio, retiro o reintegro debe estar Pagado para registrarse.', 'status'); return;
    }
    if (settled && !raw.settlementDate) { this.showIssue('Indica la fecha efectiva del cobro o pago', 'settlementDate'); return; }
    if (this.needsPartner && !raw.partnerId) { this.showIssue('Selecciona el socio', 'partnerId'); return; }
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
    const type = this.form.controls.movementType.value;
    const categories = this.data.categorias.filter(x => x.movementType === type && (x.isActive || x.id === this.data.movimiento?.categoryId));
    const current = this.data.movimiento;
    if (current && current.movementType === type && !this.data.categorias.some(x => x.id === current.categoryId)) {
      categories.push({ id: current.categoryId, name: current.categoryName, movementType: current.movementType, isActive: false });
    }
    return categories;
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
