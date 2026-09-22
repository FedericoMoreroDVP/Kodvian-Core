import { Component, Inject, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { formatDateToIso, parseIsoDate } from '../../../../core/date.utils';
import { ContratoDesarrollador, ContratoDesarrolladorFormulario, DesarrolladorExterno, ModoPagoContrato } from '../../models/proyectos.models';

interface ContratoDialogData {
  developers: DesarrolladorExterno[];
  contract?: ContratoDesarrollador;
  initialDeveloperId?: string;
}

@Component({
  selector: 'app-contrato-desarrollador-form-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatDatepickerModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  templateUrl: './contrato-desarrollador-form-dialog.component.html',
  styleUrl: './contrato-desarrollador-form-dialog.component.scss'
})
export class ContratoDesarrolladorFormDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<ContratoDesarrolladorFormDialogComponent>);

  readonly paymentModes: { value: ModoPagoContrato; label: string }[] = [
    { value: 'Percentage', label: 'Porcentaje mensual' },
    { value: 'FixedAmount', label: 'Monto fijo' }
  ];

  readonly form = this.fb.group({
    developerId: ['', [Validators.required]],
    paymentMode: ['Percentage' as ModoPagoContrato, [Validators.required]],
    percentage: [null as number | null],
    agreedAmount: [null as number | null],
    currency: [''],
    startDate: [null as Date | null, [Validators.required]],
    endDate: [null as Date | null],
    isActive: [true],
    notes: ['', [Validators.maxLength(1000)]]
  });

  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: ContratoDialogData) {
    if (data.contract) {
      this.form.patchValue({
        developerId: data.contract.developerId,
        paymentMode: data.contract.paymentMode,
        percentage: data.contract.percentage ?? null,
        agreedAmount: data.contract.agreedAmount ?? null,
        currency: data.contract.currency ?? '',
        startDate: parseIsoDate(data.contract.startDate),
        endDate: parseIsoDate(data.contract.endDate),
        isActive: data.contract.isActive,
        notes: data.contract.notes ?? ''
      });
    } else if (data.initialDeveloperId) {
      this.form.patchValue({ developerId: data.initialDeveloperId });
    }
  }

  guardar(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    if (raw.paymentMode === 'FixedAmount' && !raw.currency) { this.form.controls.currency.setErrors({ required: true }); return; }
    const payload: ContratoDesarrolladorFormulario = {
      developerId: raw.developerId ?? '',
      paymentMode: (raw.paymentMode ?? 'Percentage') as ModoPagoContrato,
      percentage: raw.paymentMode === 'Percentage' ? Number(raw.percentage) : null,
      agreedAmount: raw.paymentMode === 'FixedAmount' ? Number(raw.agreedAmount) : null,
      currency: raw.paymentMode === 'FixedAmount' ? raw.currency : null,
      startDate: formatDateToIso(raw.startDate) ?? '',
      endDate: formatDateToIso(raw.endDate),
      isActive: raw.isActive ?? true,
      notes: raw.notes || undefined
    };

    this.dialogRef.close(payload);
  }
}
