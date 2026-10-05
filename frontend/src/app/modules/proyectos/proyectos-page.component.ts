import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { AuthSessionService } from '../../core/auth/auth-session.service';

import { ProyectoDetailDialogComponent } from './components/proyecto-detail-dialog/proyecto-detail-dialog.component';
import { ProyectoDevelopersDialogComponent } from './components/proyecto-developers-dialog/proyecto-developers-dialog.component';
import { ProyectoFormDialogComponent } from './components/proyecto-form-dialog/proyecto-form-dialog.component';
import { EstadoProyecto, LookupItem, PrioridadProyecto, ProyectoDetalle, ProyectoFormulario, ProyectoListado, ProyectoLookups } from './models/proyectos.models';
import { ProyectosService } from './services/proyectos.service';

@Component({
  selector: 'app-proyectos-page',
  standalone: true,
  imports: [ReactiveFormsModule, MatCardModule, MatTableModule, MatPaginatorModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule, MatMenuModule, MatSnackBarModule],
  templateUrl: './proyectos-page.component.html',
  styleUrl: './proyectos-page.component.scss'
})
export class ProyectosPageComponent implements OnInit {
  private readonly proyectosService = inject(ProyectosService);
  private readonly fb = inject(FormBuilder);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(AuthSessionService);
  get canScheduleMeetings(): boolean { return this.session.user?.permissions.includes('meetings.write') ?? false; }

  private debeAbrirNuevoProyecto = false;

  readonly estados: { value: EstadoProyecto; label: string }[] = [
    { value: 'Planificacion', label: 'Planificación' },
    { value: 'Presupuestado', label: 'Presupuestado' },
    { value: 'EnCurso', label: 'En curso' },
    { value: 'Pausado', label: 'Pausado' },
    { value: 'Finalizado', label: 'Finalizado' },
    { value: 'Cancelado', label: 'Cancelado' }
  ];
  readonly prioridades: PrioridadProyecto[] = ['Baja', 'Media', 'Alta', 'Urgente'];
  readonly columnas = ['name', 'client', 'responsible', 'status', 'priority', 'progress', 'actions'];

  readonly filtrosForm = this.fb.group({ search: [''], clientId: [''], status: [''], priority: [''] });

  proyectos: ProyectoListado[] = [];
  clientes: LookupItem[] = [];
  responsables: LookupItem[] = [];
  pageNumber = 1;
  pageSize = 10;
  total = 0;
  cargando = false;

  ngOnInit(): void {
    this.debeAbrirNuevoProyecto = this.route.snapshot.queryParamMap.get('accion') === 'nuevo';
    this.cargarLookups();
    this.cargarProyectos();
  }

  cargarLookups(): void {
    this.proyectosService.obtenerLookups().subscribe({
      next: (data: ProyectoLookups) => {
        this.clientes = data.clients;
        this.responsables = data.responsibles;
        this.ejecutarAccionInicial();
      },
      error: () => {
        this.snackBar.open('No se pudieron cargar los datos de referencia', 'Cerrar', { duration: 3500 });
        this.ejecutarAccionInicial();
      }
    });
  }

  private ejecutarAccionInicial(): void {
    if (!this.debeAbrirNuevoProyecto) {
      return;
    }

    this.debeAbrirNuevoProyecto = false;
    this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    this.abrirNuevoProyecto();
  }

  cargarProyectos(): void {
    this.cargando = true;
    this.proyectosService.obtenerListado({ pageNumber: this.pageNumber, pageSize: this.pageSize, search: this.filtrosForm.value.search?.trim() || undefined, clientId: this.filtrosForm.value.clientId || undefined, status: (this.filtrosForm.value.status as EstadoProyecto | '') || undefined, priority: (this.filtrosForm.value.priority as PrioridadProyecto | '') || undefined }).subscribe({
      next: (result) => {
        this.proyectos = result.items;
        this.total = result.totalCount;
        this.cargando = false;
      },
      error: () => {
        this.proyectos = [];
        this.total = 0;
        this.cargando = false;
        this.snackBar.open('Ocurrió un error al cargar los proyectos', 'Cerrar', { duration: 3500 });
      }
    });
  }

  aplicarFiltros(): void { this.pageNumber = 1; this.cargarProyectos(); }
  limpiarFiltros(): void { this.filtrosForm.reset({ search: '', clientId: '', status: '', priority: '' }); this.pageNumber = 1; this.cargarProyectos(); }
  cambiarPagina(event: PageEvent): void { this.pageNumber = event.pageIndex + 1; this.pageSize = event.pageSize; this.cargarProyectos(); }

  abrirNuevoProyecto(): void {
    const dialogRef = this.dialog.open(ProyectoFormDialogComponent, {
      width: '980px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { clientes: this.clientes, responsables: this.responsables }
    });
    dialogRef.afterClosed().subscribe((formValue?: ProyectoFormulario) => {
      if (!formValue) return;
      this.proyectosService.crear(formValue).subscribe({
        next: () => { this.snackBar.open('El proyecto se creó correctamente', 'Cerrar', { duration: 3000 }); this.cargarProyectos(); },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'Ocurrió un error al guardar', 'Cerrar', { duration: 3500 })
      });
    });
  }

  verDetalle(row: ProyectoListado): void {
    this.proyectosService.obtenerDetalle(row.id).subscribe({
      next: (detalle: ProyectoDetalle) => this.dialog.open(ProyectoDetailDialogComponent, {
        width: '980px',
        maxWidth: 'calc(100vw - 32px)',
        maxHeight: 'calc(100vh - 32px)',
        autoFocus: false,
        data: detalle
      }),
      error: () => this.snackBar.open('No se pudo obtener el detalle del proyecto', 'Cerrar', { duration: 3500 })
    });
  }

  editar(row: ProyectoListado): void {
    this.proyectosService.obtenerDetalle(row.id).subscribe({
      next: (detalle: ProyectoDetalle) => {
        const dialogRef = this.dialog.open(ProyectoFormDialogComponent, {
          width: '980px',
          maxWidth: 'calc(100vw - 32px)',
          maxHeight: 'calc(100vh - 32px)',
          autoFocus: false,
          data: { proyecto: detalle, clientes: this.clientes, responsables: this.responsables }
        });
        dialogRef.afterClosed().subscribe((formValue?: ProyectoFormulario) => {
          if (!formValue) return;
          this.proyectosService.actualizar(row.id, formValue).subscribe({
            next: () => { this.snackBar.open('El proyecto se actualizó correctamente', 'Cerrar', { duration: 3000 }); this.cargarProyectos(); },
            error: (error) => this.snackBar.open(error?.error?.message ?? 'Ocurrió un error al guardar', 'Cerrar', { duration: 3500 })
          });
        });
      },
      error: () => this.snackBar.open('No se pudo cargar la información para editar', 'Cerrar', { duration: 3500 })
    });
  }

  administrarDesarrolladores(row: ProyectoListado): void {
    this.dialog.open(ProyectoDevelopersDialogComponent, {
      width: '1240px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { project: row }
    }).afterClosed().subscribe(() => this.cargarProyectos());
  }

  agendarReunion(row: ProyectoListado): void {
    this.router.navigate(['/agenda'], { queryParams: { projectId: row.id, action: 'new' } });
  }

  mostrarEstado(estado: EstadoProyecto): string {
    if (estado === 'Planificacion') return 'Planificación';
    if (estado === 'Presupuestado') return 'Presupuestado';
    if (estado === 'EnCurso') return 'En curso';
    return estado;
  }
}
