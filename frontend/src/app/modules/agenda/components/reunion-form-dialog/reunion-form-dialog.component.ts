import { Component, Inject, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AgendaLookupItem, EstadoReunion, PrioridadReunion, Reunion, ReunionFormulario } from '../../models/agenda.models';
import { AgendaService } from '../../services/agenda.service';

interface ReunionFormData { reunion?: Reunion; projects: AgendaLookupItem[]; users: AgendaLookupItem[]; projectId?: string; startsAt?: Date; }

@Component({
  selector: 'app-reunion-form-dialog', standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './reunion-form-dialog.component.html', styleUrl: './reunion-form-dialog.component.scss'
})
export class ReunionFormDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly agenda = inject(AgendaService);
  readonly dialogRef = inject(MatDialogRef<ReunionFormDialogComponent>);
  saving = false;
  error = '';
  readonly prioridades: PrioridadReunion[] = ['Baja', 'Media', 'Alta', 'Urgente'];
  readonly estados: EstadoReunion[] = ['Programada', 'Confirmada', 'Realizada'];
  readonly form = this.fb.group({
    projectId: ['', Validators.required], title: ['', [Validators.required, Validators.maxLength(200)]], description: ['', Validators.maxLength(2000)],
    date: ['', Validators.required], startTime: ['', Validators.required], endTime: ['', Validators.required],
    priority: ['Media' as PrioridadReunion, Validators.required], status: ['Programada' as EstadoReunion, Validators.required],
    link: ['', Validators.maxLength(2048)], location: ['', Validators.maxLength(300)], participantIds: [[] as string[]]
  });

  constructor(@Inject(MAT_DIALOG_DATA) readonly data: ReunionFormData) {
    const source = data.reunion;
    const start = source ? new Date(source.startsAt) : data.startsAt ?? new Date();
    const end = source ? new Date(source.endsAt) : new Date(start.getTime() + 60 * 60 * 1000);
    this.form.patchValue({
      projectId: source?.projectId ?? data.projectId ?? '', title: source?.title ?? '', description: source?.description ?? '',
      date: formatDate(start), startTime: formatTime(start), endTime: formatTime(end), priority: source?.priority ?? 'Media', status: source?.status ?? 'Programada',
      link: source?.link ?? '', location: source?.location ?? '', participantIds: source?.participants.map(x => x.id) ?? []
    });
  }

  guardar(): void {
    if (this.saving) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const raw = this.form.getRawValue();
    const startsAt = new Date(`${raw.date}T${raw.startTime}`).toISOString();
    const endsAt = new Date(`${raw.date}T${raw.endTime}`).toISOString();
    if (endsAt <= startsAt) { this.error = 'La hora de finalización debe ser posterior al inicio.'; return; }
    this.saving = true; this.error = '';
    const payload: ReunionFormulario = { projectId: raw.projectId!, title: raw.title!, description: raw.description || undefined, startsAt, endsAt,
      priority: raw.priority!, status: raw.status!, link: raw.link || undefined, location: raw.location || undefined, participantIds: raw.participantIds ?? [] };
    const request = this.data.reunion ? this.agenda.actualizar(this.data.reunion.id, payload) : this.agenda.crear(payload);
    request.subscribe({ next: () => this.dialogRef.close(true), error: error => { this.saving = false; this.error = error?.error?.message ?? 'No se pudo guardar la reunión.'; } });
  }
}

function formatDate(date: Date): string { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`; }
function formatTime(date: Date): string { return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`; }
