import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';

import { AuthSessionService } from '../../core/auth/auth-session.service';
import { AnalistaFormDialogComponent } from './components/analista-form-dialog/analista-form-dialog.component';
import { DesarrolladorFormDialogComponent } from './components/desarrollador-form-dialog/desarrollador-form-dialog.component';
import { DesarrolladorExterno, DesarrolladorFormulario, ResumenContratoDesarrollador, TeamUser, TeamUserFormulario } from './models/desarrolladores.models';
import { DesarrolladoresService } from './services/desarrolladores.service';

@Component({
  selector: 'app-desarrolladores-page',
  standalone: true,
  imports: [ReactiveFormsModule, MatCardModule, MatTableModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatSnackBarModule, CurrencyPipe, DatePipe],
  templateUrl: './desarrolladores-page.component.html',
  styleUrl: './desarrolladores-page.component.scss'
})
export class DesarrolladoresPageComponent implements OnInit {
  private readonly desarrolladoresService = inject(DesarrolladoresService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);
  private readonly authSession = inject(AuthSessionService);

  readonly columnas = ['fullName', 'email', 'phone', 'taxId', 'status', 'access', 'actions'];
  readonly columnasAnalistas = ['fullName', 'email', 'role', 'status', 'actions'];
  readonly filtrosForm = this.fb.group({ search: [''] });

  desarrolladores: DesarrolladorExterno[] = [];
  filtrados: DesarrolladorExterno[] = [];
  analistas: TeamUser[] = [];
  analistasFiltrados: TeamUser[] = [];
  resumenContratos: ResumenContratoDesarrollador[] = [];
  selectedDeveloper: DesarrolladorExterno | null = null;
  summaryYear = new Date().getFullYear();
  cargando = false;
  cargandoResumen = false;

  get canViewEconomics(): boolean {
    return this.authSession.user?.permissions.includes('finances.read') ?? false;
  }

  get canWriteTeam(): boolean { return this.authSession.user?.permissions.includes('team.write') ?? false; }
  canEdit(roles: string[] = []): boolean {
    return this.canWriteTeam && (!roles.includes('Administrador') || this.authSession.user?.roles.includes('Administrador') === true);
  }

  ngOnInit(): void {
    this.cargarDesarrolladores();
    this.filtrosForm.valueChanges.subscribe(() => this.aplicarFiltroLocal());
  }

  cargarDesarrolladores(): void {
    this.cargando = true;
    this.desarrolladoresService.obtenerListado().subscribe({
      next: (data) => {
        this.desarrolladores = data;
        this.aplicarFiltroLocal();
        this.cargando = false;
      },
      error: () => {
        this.desarrolladores = [];
        this.filtrados = [];
        this.cargando = false;
        this.snackBar.open('No se pudieron cargar los desarrolladores', 'Cerrar', { duration: 3500 });
      }
    });

    this.desarrolladoresService.obtenerAnalistas().subscribe({
      next: (data) => {
        this.analistas = data;
        this.aplicarFiltroLocal();
      },
      error: () => {
        this.analistas = [];
        this.analistasFiltrados = [];
        this.snackBar.open('No se pudieron cargar los analistas', 'Cerrar', { duration: 3500 });
      }
    });
  }

  nuevoDesarrollador(): void {
    if (!this.canWriteTeam) return;
    const ref = this.dialog.open(DesarrolladorFormDialogComponent, {
      width: '980px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false
    });

    ref.afterClosed().subscribe((payload?: DesarrolladorFormulario) => {
      if (!payload) return;

      this.desarrolladoresService.crear(payload).subscribe({
        next: () => {
          this.snackBar.open('Desarrollador creado correctamente', 'Cerrar', { duration: 3000 });
          this.cargarDesarrolladores();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo crear el desarrollador', 'Cerrar', { duration: 3500 })
      });
    });
  }

  editarDesarrollador(row: DesarrolladorExterno): void {
    if (!this.canEdit(row.roles)) return;
    const ref = this.dialog.open(DesarrolladorFormDialogComponent, {
      width: '980px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { desarrollador: row }
    });

    ref.afterClosed().subscribe((payload?: DesarrolladorFormulario) => {
      if (!payload) return;

      this.desarrolladoresService.actualizar(row.id, payload).subscribe({
        next: () => {
          this.snackBar.open('Desarrollador actualizado correctamente', 'Cerrar', { duration: 3000 });
          this.cargarDesarrolladores();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo actualizar el desarrollador', 'Cerrar', { duration: 3500 })
      });
    });
  }

  nuevoAnalista(): void {
    if (!this.canWriteTeam) return;
    const ref = this.dialog.open(AnalistaFormDialogComponent, {
      width: '760px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false
    });

    ref.afterClosed().subscribe((payload?: TeamUserFormulario) => {
      if (!payload) return;

      this.desarrolladoresService.crearAnalista(payload).subscribe({
        next: () => {
          this.snackBar.open('Analista creado correctamente', 'Cerrar', { duration: 3000 });
          this.cargarDesarrolladores();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo crear el analista', 'Cerrar', { duration: 3500 })
      });
    });
  }

  editarAnalista(row: TeamUser): void {
    if (!this.canEdit(row.roles)) return;
    const ref = this.dialog.open(AnalistaFormDialogComponent, {
      width: '760px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100vh - 32px)',
      autoFocus: false,
      data: { analista: row }
    });

    ref.afterClosed().subscribe((payload?: TeamUserFormulario) => {
      if (!payload) return;

      this.desarrolladoresService.actualizarAnalista(row.id, payload).subscribe({
        next: () => {
          this.snackBar.open('Analista actualizado correctamente', 'Cerrar', { duration: 3000 });
          this.cargarDesarrolladores();
        },
        error: (error) => this.snackBar.open(error?.error?.message ?? 'No se pudo actualizar el analista', 'Cerrar', { duration: 3500 })
      });
    });
  }

  toggleResumen(row: DesarrolladorExterno, event?: Event): void {
    event?.stopPropagation();
    if (!this.canViewEconomics) {
      return;
    }

    if (this.selectedDeveloper?.id === row.id) {
      this.selectedDeveloper = null;
      this.resumenContratos = [];
      return;
    }

    this.selectedDeveloper = row;
    this.cargarResumen(row.id);
  }

  private cargarResumen(developerId: string): void {
    this.cargandoResumen = true;
    this.resumenContratos = [];

    this.desarrolladoresService.obtenerResumenContratos(developerId, this.summaryYear).subscribe({
      next: (data) => {
        this.resumenContratos = data;
        this.cargandoResumen = false;
      },
      error: (error) => {
        this.cargandoResumen = false;
        this.snackBar.open(error?.error?.message ?? 'No se pudo cargar el resumen del desarrollador', 'Cerrar', { duration: 3500 });
      }
    });
  }

  obtenerModalidad(value: ResumenContratoDesarrollador): string {
    return value.paymentMode === 'Percentage' ? 'Porcentaje' : 'Monto fijo';
  }

  estadoPago(value: ResumenContratoDesarrollador): string {
    return value.isUpToDate ? 'Al dia' : 'Pendiente';
  }

  private aplicarFiltroLocal(): void {
    const search = (this.filtrosForm.value.search ?? '').trim().toLowerCase();
    if (!search) {
      this.filtrados = [...this.desarrolladores];
      this.analistasFiltrados = [...this.analistas];
      return;
    }

    this.filtrados = this.desarrolladores.filter((x) =>
      x.fullName.toLowerCase().includes(search)
      || (x.email ?? '').toLowerCase().includes(search)
      || (x.phone ?? '').toLowerCase().includes(search)
      || (x.taxId ?? '').toLowerCase().includes(search));

    this.analistasFiltrados = this.analistas.filter((x) =>
      x.fullName.toLowerCase().includes(search)
      || x.email.toLowerCase().includes(search)
      || x.role.toLowerCase().includes(search));
  }
}
