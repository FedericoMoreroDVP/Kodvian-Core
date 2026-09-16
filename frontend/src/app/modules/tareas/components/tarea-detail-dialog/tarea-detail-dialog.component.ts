import { Component, Inject, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { AuthSessionService } from '../../../../core/auth/auth-session.service';
import { TareaAttachmentsComponent } from '../tarea-attachments/tarea-attachments.component';
import { MatButtonModule } from '@angular/material/button';

import { TareaDetalle } from '../../models/tareas.models';

@Component({
  selector: 'app-tarea-detail-dialog',
  standalone: true,
  imports: [TareaAttachmentsComponent, MatDialogModule, MatButtonModule],
  templateUrl: './tarea-detail-dialog.component.html',
  styleUrl: './tarea-detail-dialog.component.scss'
})
export class TareaDetailDialogComponent {
  private readonly session = inject(AuthSessionService);
  private readonly dialogRef = inject(MatDialogRef<TareaDetailDialogComponent>);
  busy = false;
  get canWriteAttachments(): boolean {
    const user = this.session.user;
    return !!user && (user.permissions.includes('tasks.write') ||
      (this.tarea.isActive && !!user.developerId && user.developerId === this.tarea.developerId && user.permissions.includes('developer.tasks.status.write')));
  }
  setBusy(value: boolean): void { this.busy = value; this.dialogRef.disableClose = value; }
  constructor(@Inject(MAT_DIALOG_DATA) public readonly tarea: TareaDetalle) {}

  valor(texto?: string | number | null): string {
    return texto === null || texto === undefined || texto === '' ? '-' : String(texto);
  }

  mostrarEstado(estado: string): string {
    if (estado === 'EnCurso') return 'En curso';
    return estado;
  }
}
