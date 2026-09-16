import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthSessionService } from '../../core/auth/auth-session.service';
import { ManagedUser, UsersService } from './users.service';
import { UserRolesDialogComponent } from './user-roles-dialog.component';

@Component({
  selector: 'app-administracion-page',
  standalone: true,
  imports: [MatCardModule, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatPaginatorModule],
  templateUrl: './administracion-page.component.html',
  styleUrl: './administracion-page.component.scss'
})
export class AdministracionPageComponent implements OnInit, OnDestroy {
  private readonly api = inject(UsersService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly session = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly subscriptions = new Subscription();
  private listing?: Subscription;
  users: ManagedUser[] = [];
  roles: string[] = [];
  search = '';
  pageNumber = 1;
  pageSize = 20;
  total = 0;
  loading = false;
  error = '';
  rolesError = '';
  ngOnInit(): void { this.load(); this.loadRoles(); }
  ngOnDestroy(): void { this.subscriptions.unsubscribe(); this.listing?.unsubscribe(); }
  loadRoles(): void {
    this.rolesError = '';
    this.subscriptions.add(this.api.roles().subscribe({ next: roles => this.roles = roles, error: () => this.rolesError = 'No se pudieron cargar los roles disponibles' }));
  }
  load(): void {
    this.listing?.unsubscribe(); this.loading = true; this.error = '';
    this.listing = this.api.list(this.pageNumber, this.pageSize, this.search).subscribe({
      next: data => { this.users = data.items; this.total = data.totalCount; this.loading = false; },
      error: () => { this.loading = false; this.error = 'No se pudieron cargar los usuarios'; }
    });
  }
  filter(): void { this.pageNumber = 1; this.load(); }
  page(event: PageEvent): void { this.pageNumber = event.pageIndex + 1; this.pageSize = event.pageSize; this.load(); }
  edit(user: ManagedUser): void {
    if (!this.roles.length) return;
    this.subscriptions.add(this.dialog.open(UserRolesDialogComponent, { width: '580px', data: { user, roles: this.roles } }).afterClosed().subscribe((updated?: ManagedUser) => {
      if (!updated) return;
      this.snack.open('Roles actualizados. Se requiere un nuevo inicio de sesión para esa cuenta.', 'Cerrar', { duration: 4500 });
      if (updated.id === this.session.user?.id) { this.session.clearSession(); void this.router.navigate(['/login']); }
      else this.load();
    }));
  }
}
