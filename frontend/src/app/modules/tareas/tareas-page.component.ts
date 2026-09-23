import { Component, OnInit, inject } from '@angular/core';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { AuthSessionService } from '../../core/auth/auth-session.service';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';

import { TareaDetailDialogComponent } from './components/tarea-detail-dialog/tarea-detail-dialog.component';
import { TareaFormDialogComponent } from './components/tarea-form-dialog/tarea-form-dialog.component';
import { TareaStatusDialogComponent } from './components/tarea-status-dialog/tarea-status-dialog.component';
import { compareDates, formatDateToIso } from '../../core/date.utils';
import { EstadoTarea, KanbanColumn, LookupItem, PrioridadTarea, TareaDetalle, TareaListado, TareaLookups } from './models/tareas.models';
import { TareasService } from './services/tareas.service';

@Component({
  selector: 'app-tareas-page',
  standalone: true,
  imports: [DragDropModule, ReactiveFormsModule, MatCardModule, MatTableModule, MatPaginatorModule, MatFormFieldModule, MatDatepickerModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule, MatSnackBarModule],
  templateUrl: './tareas-page.component.html',
  styleUrl: './tareas-page.component.scss'
})
export class TareasPageComponent implements OnInit {
  private readonly tareasService = inject(TareasService);
  private readonly fb = inject(FormBuilder);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(AuthSessionService);
  movingId: string | null = null;
  deletingId: string | null = null;
  get canWrite(): boolean { return this.session.user?.permissions.includes('tasks.write') ?? false; }

  private debeAbrirNuevaTarea = false;

  readonly estados: { value: EstadoTarea; label: string }[] = [
    { value: 'Pendiente', label: 'Pendiente' },
    { value: 'EnCurso', label: 'En curso' },
    { value: 'Bloqueada', label: 'Bloqueada' },
    { value: 'Finalizada', label: 'Finalizada' },
    { value: 'Cancelada', label: 'Cancelada' }
  ];
  readonly prioridades: PrioridadTarea[] = ['Baja', 'Media', 'Alta', 'Urgente'];
  readonly columnas = ['title', 'project', 'developer', 'status', 'priority', 'dueDate', 'actions'];

  readonly filtrosForm = this.fb.group({
    search: [''],
    projectId: [''],
    developerId: [''],
    status: [''],
    priority: [''],
    dueDateFrom: [null as Date | null],
    dueDateTo: [null as Date | null]
  });

  vista: 'lista' | 'kanban' = 'kanban';
  tareas: TareaListado[] = [];
  kanban: KanbanColumn[] = [];
  projects: LookupItem[] = [];
  developers: LookupItem[] = [];
  pageNumber = 1;
  pageSize = 10;
  total = 0;
  cargando = false;

  ngOnInit(): void {
    this.debeAbrirNuevaTarea = this.route.snapshot.queryParamMap.get('accion') === 'nuevo';
    this.cargarLookups();
    this.cargarDatos();
  }

  cambiarVista(vista: 'lista' | 'kanban'): void {
    if (this.movingId) return;
    this.vista = vista;
    this.cargarDatos();
  }

  cargarLookups(): void {
    this.tareasService.obtenerLookups().subscribe({
      next: (data: TareaLookups) => {
        this.projects = data.projects;
        this.developers = data.developers;
        this.ejecutarAccionInicial();
      },
      error: () => {
        this.snackBar.open('No se pudieron cargar los datos de referencia', 'Cerrar', { duration: 3500 });
        this.ejecutarAccionInicial();
      }
    });
  }

  private ejecutarAccionInicial(): void {
    if (!this.debeAbrirNuevaTarea) {
      return;
    }

    this.debeAbrirNuevaTarea = false;
    this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    this.abrirNuevaTarea();
  }

  cargarDatos(): void {
    if (this.movingId) return;
    if (this.vista === 'kanban') {
      this.cargarKanban();
      return;
    }

    this.cargarLista();
  }

  private cargarLista(): void {
    this.cargando = true;
    this.tareasService.obtenerListado(this.obtenerFiltros()).subscribe({
      next: (result) => {
        this.tareas = result.items;
        this.total = result.totalCount;
        this.cargando = false;
      },
      error: () => {
        this.tareas = [];
        this.total = 0;
        this.cargando = false;
        this.snackBar.open('Ocurrió un error al cargar las tareas', 'Cerrar', { duration: 3500 });
      }
    });
  }

  private cargarKanban(): void {
    this.cargando = true;
    this.tareasService.obtenerKanban(this.obtenerFiltros()).subscribe({
      next: (result) => {
        this.kanban = result;
        this.cargando = false;
      },
      error: () => {
        this.kanban = [];
        this.cargando = false;
        this.snackBar.open('Ocurrió un error al cargar el tablero', 'Cerrar', { duration: 3500 });
      }
    });
  }

  aplicarFiltros(): void {
    if (this.movingId) return;
    if (!this.rangoFechasValido()) {
      this.snackBar.open('La fecha desde no puede ser mayor que la fecha hasta', 'Cerrar', { duration: 3500 });
      return;
    }

    this.pageNumber = 1;
    this.cargarDatos();
  }

  limpiarFiltros(): void {
    if (this.movingId) return;
    this.filtrosForm.reset({ search: '', projectId: '', developerId: '', status: '', priority: '', dueDateFrom: null, dueDateTo: null });
    this.pageNumber = 1;
    this.cargarDatos();
  }

  cambiarPagina(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.cargarLista();
  }

  abrirNuevaTarea(): void {
    if (!this.canWrite) return;
    const dialogRef = this.dialog.open(TareaFormDialogComponent, {
      width: '960px',
      data: { projects: this.projects, developers: this.developers }
    });

    dialogRef.afterClosed().subscribe((changed?: boolean) => {
      if (changed) this.cargarDatos();
    });
  }

  verDetalle(id: string): void {
    this.tareasService.obtenerDetalle(id).subscribe({
      next: (detalle: TareaDetalle) => this.dialog.open(TareaDetailDialogComponent, { width: '820px', data: detalle }),
      error: () => this.snackBar.open('No se pudo obtener el detalle de la tarea', 'Cerrar', { duration: 3500 })
    });
  }

  editar(id: string): void {
    if (!this.canWrite || this.movingId || this.deletingId) return;
    this.tareasService.obtenerDetalle(id).subscribe({
      next: (detalle: TareaDetalle) => {
        const dialogRef = this.dialog.open(TareaFormDialogComponent, {
          width: '960px',
          data: { tarea: detalle, projects: this.projects, developers: this.developers }
        });

        dialogRef.afterClosed().subscribe((changed?: boolean) => {
          if (changed) this.cargarDatos();
        });
      },
      error: () => this.snackBar.open('No se pudo cargar la tarea para editar', 'Cerrar', { duration: 3500 })
    });
  }

  cambiarEstado(id: string, actual: EstadoTarea): void {
    if (!this.canWrite || this.deletingId) return;
    const dialogRef = this.dialog.open(TareaStatusDialogComponent, {
      width: '420px',
      data: { statusActual: actual }
    });

    dialogRef.afterClosed().subscribe((estado?: EstadoTarea) => {
      if (!estado) return;

      const order = this.tareas.find(t => t.id === id)?.kanbanOrder ?? 0;
      this.tareasService.actualizarEstado(id, estado, order).subscribe({
        next: () => {
          this.snackBar.open('El estado de la tarea se actualizó correctamente', 'Cerrar', { duration: 3000 });
          this.cargarDatos();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'Ocurrió un error al actualizar estado', 'Cerrar', { duration: 3500 })
      });
    });
  }

  mostrarEstado(estado: EstadoTarea): string {
    if (estado === 'EnCurso') return 'En curso';
    return estado;
  }

  private obtenerFiltros() {
    return {
      pageNumber: this.pageNumber,
      pageSize: this.pageSize,
      search: this.filtrosForm.value.search?.trim() || undefined,
      projectId: this.filtrosForm.value.projectId || undefined,
      developerId: this.filtrosForm.value.developerId || undefined,
      status: (this.filtrosForm.value.status as EstadoTarea | '') || undefined,
      priority: (this.filtrosForm.value.priority as PrioridadTarea | '') || undefined,
      dueDateFrom: formatDateToIso(this.filtrosForm.value.dueDateFrom) || undefined,
      dueDateTo: formatDateToIso(this.filtrosForm.value.dueDateTo) || undefined
    };
  }

  eliminar(id: string, status: EstadoTarea, title: string): void {
    if (!this.canWrite || this.movingId || this.deletingId || status !== 'Cancelada') return;
    if (!confirm(`Eliminar permanentemente "${title}" y todos sus adjuntos? Esta acción no se puede deshacer.`)) return;

    this.deletingId = id;
    this.tareasService.eliminar(id).subscribe({
      next: () => {
        this.deletingId = null;
        this.snackBar.open('La tarea cancelada y sus adjuntos se eliminaron permanentemente', 'Cerrar', { duration: 3500 });
        this.cargarDatos();
      },
      error: (error) => {
        this.deletingId = null;
        this.snackBar.open(error?.error?.message ?? 'No se pudo eliminar la tarea cancelada', 'Cerrar', { duration: 4000 });
      }
    });
  }

  moverTarjeta(event: CdkDragDrop<KanbanColumn>): void {
    if (!event.isPointerOverContainer || event.previousContainer === event.container || !this.canWrite || this.movingId || this.deletingId) return;
    const source = event.previousContainer.data;
    const target = event.container.data;
    const item = event.item.data as KanbanColumn['items'][number];
    const sourceIndex = source.items.findIndex(t => t.id === item.id);
    if (sourceIndex < 0) return;
    source.items.splice(sourceIndex, 1);
    target.items.push(item);
    this.movingId = item.id;
    this.tareasService.actualizarEstado(item.id, target.status, item.kanbanOrder).subscribe({
      next: () => {
        this.movingId = null;
        this.snackBar.open(`Tarea movida a ${this.mostrarEstado(target.status)}`, 'Cerrar', { duration: 3000 });
        this.cargarDatos();
      },
      error: () => {
        target.items.splice(target.items.findIndex(t => t.id === item.id), 1);
        source.items.splice(sourceIndex, 0, item);
        this.movingId = null;
        this.snackBar.open('No se pudo mover la tarea. Se restauró su posición anterior.', 'Cerrar', { duration: 4500 });
      }
    });
  }

  private rangoFechasValido(): boolean {
    const dueDateFrom = this.filtrosForm.value.dueDateFrom;
    const dueDateTo = this.filtrosForm.value.dueDateTo;

    if (!dueDateFrom || !dueDateTo) {
      return true;
    }

    return compareDates(dueDateFrom, dueDateTo) <= 0;
  }
}
