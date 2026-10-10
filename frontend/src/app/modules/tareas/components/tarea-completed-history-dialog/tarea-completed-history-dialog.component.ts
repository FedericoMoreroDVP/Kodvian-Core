import { Component, Inject, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';

import { TareaDetailDialogComponent } from '../tarea-detail-dialog/tarea-detail-dialog.component';
import { EstadoTarea, TareaDetalle, TareaFiltros, TareaListado } from '../../models/tareas.models';
import { TareasService } from '../../services/tareas.service';

export interface TareaCompletedHistoryDialogData {
  filters: TareaFiltros;
}

@Component({
  selector: 'app-tarea-completed-history-dialog',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatDialogModule, MatPaginatorModule],
  templateUrl: './tarea-completed-history-dialog.component.html',
  styleUrl: './tarea-completed-history-dialog.component.scss'
})
export class TareaCompletedHistoryDialogComponent implements OnInit {
  private readonly tareasService = inject(TareasService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  tasks: TareaListado[] = [];
  loading = false;
  pageNumber = 1;
  pageSize = 20;
  total = 0;

  constructor(@Inject(MAT_DIALOG_DATA) readonly data: TareaCompletedHistoryDialogData) {}

  ngOnInit(): void { this.load(); }

  changePage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.load();
  }

  openDetail(id: string): void {
    this.tareasService.obtenerDetalle(id).subscribe({
      next: (task: TareaDetalle) => this.dialog.open(TareaDetailDialogComponent, { width: '820px', maxWidth: 'calc(100vw - 24px)', data: task }),
      error: () => this.snackBar.open('No se pudo obtener el detalle de la tarea', 'Cerrar', { duration: 3500 })
    });
  }

  private load(): void {
    this.loading = true;
    this.tareasService.obtenerListado({ ...this.data.filters, status: 'Finalizada' as EstadoTarea, completedHistory: true, pageNumber: this.pageNumber, pageSize: this.pageSize }).subscribe({
      next: result => { this.tasks = result.items; this.total = result.totalCount; this.loading = false; },
      error: () => { this.tasks = []; this.total = 0; this.loading = false; this.snackBar.open('No se pudo cargar el historial de finalizadas', 'Cerrar', { duration: 3500 }); }
    });
  }
}
