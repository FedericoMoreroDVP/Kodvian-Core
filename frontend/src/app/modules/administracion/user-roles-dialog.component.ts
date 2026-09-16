import { Component, Inject, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { UsersService, ManagedUser } from './users.service';

@Component({
  selector: 'app-user-roles-dialog', standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>Modificar roles</h2>
    <mat-dialog-content>
      <p><strong>{{ data.user.fullName }}</strong><br />{{ data.user.email }}</p>
      <p>Selecciona las funciones de esta persona. Administrador se puede combinar con Analista y Desarrollador.</p>
      <div class="roles">
        @for (role of data.roles; track role) {
          <mat-checkbox [checked]="selected.includes(role)" [disabled]="saving" (change)="toggle(role, $event.checked)">{{ role }}</mat-checkbox>
        }
      </div>
      <p>Solo lectura es exclusivo. Al cambiar los roles, el usuario deberá iniciar sesión nuevamente.</p>
      @if (!valid) { <p role="alert">Selecciona al menos un rol. Solo lectura no se puede combinar con otros roles.</p> }
      @if (error) { <p role="alert">{{ error }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="saving">Cancelar</button>
      <button mat-flat-button [disabled]="saving || !valid || !changed" (click)="save()">{{ saving ? 'Guardando…' : 'Guardar roles' }}</button>
    </mat-dialog-actions>`,
  styles: [`.roles { display: flex; flex-direction: column; gap: 8px; } [role=alert] { color: #ff9b9b; }`]
})
export class UserRolesDialogComponent {
  private readonly api = inject(UsersService);
  private readonly ref = inject(MatDialogRef<UserRolesDialogComponent>);
  selected: string[];
  saving = false;
  error = '';
  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: { user: ManagedUser; roles: string[] }) { this.selected = [...data.user.roles]; }
  get valid(): boolean { return this.selected.length > 0 && (!this.selected.includes('Solo lectura') || this.selected.length === 1); }
  get changed(): boolean { return [...this.selected].sort().join('|') !== [...this.data.user.roles].sort().join('|'); }
  toggle(role: string, checked: boolean): void {
    if (this.saving) return;
    this.selected = checked ? [...new Set([...this.selected, role])] : this.selected.filter(r => r !== role);
  }
  save(): void {
    if (!this.valid || !this.changed || this.saving) return;
    this.saving = true; this.ref.disableClose = true; this.error = '';
    this.api.updateRoles(this.data.user, this.selected).subscribe({
      next: user => this.ref.close(user),
      error: error => { this.saving = false; this.ref.disableClose = false; this.error = error?.error?.message ?? 'No se pudieron actualizar los roles'; }
    });
  }
}
