import { Component, Inject, ViewChild, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TareasService } from '../../services/tareas.service';
import { TareaAttachmentsComponent } from '../tarea-attachments/tarea-attachments.component';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { compareDates, formatDateToIso, parseIsoDate } from '../../../../core/date.utils';
import { EstadoTarea, LookupItem, PrioridadTarea, TareaDetalle, TareaFormulario } from '../../models/tareas.models';

interface TareaFormData {
  tarea?: TareaDetalle;
  projects: LookupItem[];
  developers: LookupItem[];
}

@Component({
  selector: 'app-tarea-form-dialog',
  standalone: true,
  imports: [TareaAttachmentsComponent, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatDatepickerModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  templateUrl: './tarea-form-dialog.component.html',
  styleUrl: './tarea-form-dialog.component.scss'
})
export class TareaFormDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<TareaFormDialogComponent>);
  private readonly tareasService = inject(TareasService);
  @ViewChild(TareaAttachmentsComponent) attachments!: TareaAttachmentsComponent;
  savedTaskId?: string;
  saving = false;
  attachmentsBusy = false;
  error = '';
  changed = false;

  readonly estados: { value: EstadoTarea; label: string }[] = [
    { value: 'Pendiente', label: 'Pendiente' },
    { value: 'EnCurso', label: 'En curso' },
    { value: 'Bloqueada', label: 'Bloqueada' },
    { value: 'Finalizada', label: 'Finalizada' },
    { value: 'Cancelada', label: 'Cancelada' }
  ];

  readonly prioridades: PrioridadTarea[] = ['Baja', 'Media', 'Alta', 'Urgente'];

  readonly form = this.fb.group({
    projectId: ['', [Validators.required]],
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(2000)]],
    developerId: [''],
    status: ['Pendiente' as EstadoTarea, [Validators.required]],
    priority: ['Media' as PrioridadTarea, [Validators.required]],
    startDate: [null as Date | null],
    dueDate: [null as Date | null],
    finishedDate: [null as Date | null],
    estimatedHours: [null as number | null, [Validators.min(0)]],
    realHours: [null as number | null, [Validators.min(0)]],
    kanbanOrder: [0],
    isActive: [true]
  }, { validators: [taskDateRangeValidator()] });

  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: TareaFormData) {
    this.savedTaskId = data.tarea?.id;
    if (data.tarea) {
      if (data.tarea.developerId && !data.developers.some(d => d.id === data.tarea!.developerId)) {
        data.developers = [...data.developers, { id: data.tarea.developerId, name: `${data.tarea.developerName ?? 'Miembro'} (asignación existente)` }];
      }
      this.form.patchValue({
        projectId: data.tarea.projectId,
        title: data.tarea.title,
        description: data.tarea.description ?? '',
        developerId: data.tarea.developerId ?? '',
        status: data.tarea.status,
        priority: data.tarea.priority,
        startDate: parseIsoDate(data.tarea.startDate),
        dueDate: parseIsoDate(data.tarea.dueDate),
        finishedDate: parseIsoDate(data.tarea.finishedDate),
        estimatedHours: data.tarea.estimatedHours ?? null,
        realHours: data.tarea.realHours ?? null,
        kanbanOrder: data.tarea.kanbanOrder,
        isActive: data.tarea.isActive
      });
    }
  }

  async guardar(): Promise<void> {
    if (this.saving || this.attachmentsBusy) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const payload = {
      projectId: raw.projectId,
      title: raw.title,
      description: raw.description || undefined,
      developerId: raw.developerId || null,
      responsibleId: this.data.tarea?.responsibleId ?? null,
      status: raw.status,
      priority: raw.priority,
      startDate: formatDateToIso(raw.startDate),
      dueDate: formatDateToIso(raw.dueDate),
      finishedDate: formatDateToIso(raw.finishedDate),
      estimatedHours: raw.estimatedHours,
      realHours: raw.realHours,
      kanbanOrder: raw.kanbanOrder ?? 0,
      isActive: !!raw.isActive
    } as TareaFormulario;
    this.saving = true;
    this.dialogRef.disableClose = true;
    this.error = '';
    this.form.disable();
    try {
      const task = await firstValueFrom(this.savedTaskId
        ? this.tareasService.actualizar(this.savedTaskId, payload)
        : this.tareasService.crear(payload));
      this.savedTaskId = task.id;
      this.changed = true;
      if (await this.attachments.uploadPending(task.id)) {
        this.dialogRef.close(true);
      } else {
        this.error = 'La tarea se guardó, pero hay archivos pendientes. Vuelve a guardar para reintentar su subida.';
      }
    } catch (error: any) {
      this.error = error?.error?.message ?? error?.error?.Message ?? 'No se pudo guardar la tarea. Tus datos se conservaron.';
    } finally {
      this.saving = false;
      this.form.enable();
      // After a partial save, close explicitly so the board always refreshes.
      this.dialogRef.disableClose = this.changed || this.attachmentsBusy;
    }
  }

  setAttachmentsBusy(busy: boolean): void {
    this.attachmentsBusy = busy;
    this.dialogRef.disableClose = busy || this.saving || this.changed;
  }
  cerrar(): void { if (!this.saving && !this.attachmentsBusy) this.dialogRef.close(this.changed); }
}

function taskDateRangeValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const startDate = control.get('startDate')?.value as Date | null;
    const dueDate = control.get('dueDate')?.value as Date | null;
    const finishedDate = control.get('finishedDate')?.value as Date | null;

    if (startDate && dueDate && compareDates(dueDate, startDate) < 0) {
      return { invalidDueDate: true };
    }

    if (startDate && finishedDate && compareDates(finishedDate, startDate) < 0) {
      return { invalidFinishedDate: true };
    }

    return null;
  };
}
