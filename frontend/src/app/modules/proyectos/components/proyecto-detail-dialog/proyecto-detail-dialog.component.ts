import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, DestroyRef, Inject, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';

import { AuthSessionService } from '../../../../core/auth/auth-session.service';
import { ProyectoDetalle } from '../../models/proyectos.models';
import { isValidGoogleDriveLink } from '../../models/project-drive-link.model';
import { ProyectosService } from '../../services/proyectos.service';

@Component({
  selector: 'app-proyecto-detail-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, CurrencyPipe, MatDialogModule, MatButtonModule],
  templateUrl: './proyecto-detail-dialog.component.html',
  styleUrl: './proyecto-detail-dialog.component.scss'
})
export class ProyectoDetailDialogComponent implements OnInit {
  private readonly proyectosService = inject(ProyectosService);
  private readonly authSession = inject(AuthSessionService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialogRef = inject(MatDialogRef<ProyectoDetailDialogComponent>);
  private readonly destroyRef = inject(DestroyRef);

  puedeLeerDocumentos = false;
  puedeEscribirDocumentos = false;
  cargandoEnlace = false;
  guardandoEnlace = false;
  enlaceCargado = false;
  enlaceDrive = '';
  enlaceGuardado: string | null = null;
  errorEnlace = '';

  constructor(@Inject(MAT_DIALOG_DATA) public readonly proyecto: ProyectoDetalle) {}

  ngOnInit(): void {
    this.authSession.ensureSessionLoaded().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => {
      const permissions = user?.permissions ?? [];
      this.puedeLeerDocumentos = permissions.includes('projects.documents.read');
      this.puedeEscribirDocumentos = this.puedeLeerDocumentos && permissions.includes('projects.documents.write');
      if (this.puedeLeerDocumentos) this.cargarEnlace();
    });
  }

  get enlaceValido(): boolean { return isValidGoogleDriveLink(this.enlaceDrive); }
  get enlaceParaAbrir(): string | null {
    return this.enlaceGuardado && isValidGoogleDriveLink(this.enlaceGuardado) ? this.enlaceGuardado : null;
  }

  cargarEnlace(): void {
    if (!this.puedeLeerDocumentos || this.cargandoEnlace || this.guardandoEnlace) return;
    this.cargandoEnlace = true;
    this.errorEnlace = '';
    this.proyectosService.obtenerEnlaceDrive(this.proyecto.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => {
        this.enlaceGuardado = data.googleDriveFolderUrl;
        this.enlaceDrive = this.enlaceGuardado ?? '';
        this.enlaceCargado = true;
        this.cargandoEnlace = false;
      },
      error: () => {
        this.cargandoEnlace = false;
        this.errorEnlace = 'No se pudo cargar el enlace de Google Drive. Puedes reintentar.';
      }
    });
  }

  guardarEnlace(quitar = false): void {
    if (!this.puedeEscribirDocumentos || this.guardandoEnlace || !this.enlaceCargado || this.cargandoEnlace) return;
    if (!quitar && !this.enlaceValido) {
      this.errorEnlace = 'Ingresa un enlace HTTPS válido de Google Drive.';
      return;
    }
    const link = quitar ? null : this.enlaceDrive.trim() || null;
    this.guardandoEnlace = true;
    this.dialogRef.disableClose = true;
    this.errorEnlace = '';
    this.proyectosService.guardarEnlaceDrive(this.proyecto.id, link).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => {
        this.enlaceGuardado = data.googleDriveFolderUrl;
        this.enlaceDrive = this.enlaceGuardado ?? '';
        this.finalizarGuardado();
        this.snackBar.open(this.enlaceGuardado ? 'Enlace guardado correctamente' : 'Enlace quitado correctamente', 'Cerrar', { duration: 3000 });
      },
      error: error => {
        this.finalizarGuardado();
        this.errorEnlace = error?.error?.message ?? 'No se pudo guardar el enlace. Lo ingresado se conservó para reintentar.';
      }
    });
  }

  private finalizarGuardado(): void {
    this.guardandoEnlace = false;
    this.dialogRef.disableClose = false;
  }

  valor(texto?: string | number | null): string {
    return texto === null || texto === undefined || texto === '' ? '-' : String(texto);
  }

  mostrarEstado(estado: string): string {
    if (estado === 'Planificacion') return 'Planificación';
    if (estado === 'EnCurso') return 'En curso';
    return estado;
  }
}
