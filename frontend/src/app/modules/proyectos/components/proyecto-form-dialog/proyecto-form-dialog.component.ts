import { Component, Inject, inject } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { compareDates, formatDateToIso, parseIsoDate } from '../../../../core/date.utils';
import { EstadoProyecto, LookupItem, PrioridadProyecto, ProyectoDetalle, ProyectoFormulario } from '../../models/proyectos.models';

interface ProyectoFormData {
  proyecto?: ProyectoDetalle;
  clientes: LookupItem[];
  responsables: LookupItem[];
}

@Component({
  selector: 'app-proyecto-form-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatDatepickerModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule
  ],
  templateUrl: './proyecto-form-dialog.component.html',
  styleUrl: './proyecto-form-dialog.component.scss'
})
export class ProyectoFormDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<ProyectoFormDialogComponent>);

  readonly estados: { value: EstadoProyecto; label: string }[] = [
    { value: 'Planificacion', label: 'Planificación' },
    { value: 'Presupuestado', label: 'Presupuestado' },
    { value: 'EnCurso', label: 'En curso' },
    { value: 'Pausado', label: 'Pausado' },
    { value: 'Finalizado', label: 'Finalizado' },
    { value: 'Cancelado', label: 'Cancelado' }
  ];

  readonly prioridades: PrioridadProyecto[] = ['Baja', 'Media', 'Alta', 'Urgente'];

  readonly form = this.fb.group({
    clientId: ['', [Validators.required]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(2000)]],
    responsibleId: [''],
    status: ['Planificacion' as EstadoProyecto, [Validators.required]],
    priority: ['Media' as PrioridadProyecto, [Validators.required]],
    startDate: [null as Date | null],
    estimatedDeliveryDate: [null as Date | null],
    closingDate: [null as Date | null],
    budget: [null as number | null, [Validators.min(0)]],
    progressPercentage: [0, [Validators.min(0), Validators.max(100)]],
    isActive: [true]
  }, { validators: [projectDateRangeValidator()] });

  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: ProyectoFormData) {
    if (data.proyecto) {
      if (data.proyecto.responsibleId && !data.responsables.some(r => r.id === data.proyecto!.responsibleId)) {
        data.responsables = [...data.responsables, { id: data.proyecto.responsibleId, name: `${data.proyecto.responsibleName ?? 'Responsable'} (asignación existente)` }];
      }
      this.form.patchValue({
        clientId: data.proyecto.clientId,
        name: data.proyecto.name,
        description: data.proyecto.description ?? '',
        responsibleId: data.proyecto.responsibleId ?? '',
        status: data.proyecto.status,
        priority: data.proyecto.priority,
        startDate: parseIsoDate(data.proyecto.startDate),
        estimatedDeliveryDate: parseIsoDate(data.proyecto.estimatedDeliveryDate),
        closingDate: parseIsoDate(data.proyecto.closingDate),
        budget: data.proyecto.budget ?? null,
        progressPercentage: data.proyecto.progressPercentage,
        isActive: data.proyecto.isActive
      });
    }
  }

  guardar(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();

    this.dialogRef.close({
      clientId: raw.clientId,
      name: raw.name,
      description: raw.description || undefined,
      responsibleId: raw.responsibleId || null,
      status: raw.status,
      priority: raw.priority,
      startDate: formatDateToIso(raw.startDate),
      estimatedDeliveryDate: formatDateToIso(raw.estimatedDeliveryDate),
      closingDate: formatDateToIso(raw.closingDate),
      budget: raw.budget,
      progressPercentage: raw.progressPercentage ?? 0,
      isActive: !!raw.isActive
    } as ProyectoFormulario);
  }
}

function projectDateRangeValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const startDate = control.get('startDate')?.value as Date | null;
    const estimatedDeliveryDate = control.get('estimatedDeliveryDate')?.value as Date | null;
    const closingDate = control.get('closingDate')?.value as Date | null;

    if (startDate && estimatedDeliveryDate && compareDates(estimatedDeliveryDate, startDate) < 0) {
      return { invalidEstimatedDeliveryDate: true };
    }

    if (startDate && closingDate && compareDates(closingDate, startDate) < 0) {
      return { invalidClosingDate: true };
    }

    return null;
  };
}
