import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

import { AuthSessionService } from './core/auth/auth-session.service';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatSnackBarModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  cargando = false;

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]]
  });

  ngOnInit(): void {
    this.authSession.ensureSessionLoaded().subscribe((user) => {
      if (user) {
        void this.router.navigate([this.getPostLoginRoute()]);
      }
    });
  }

  iniciarSesion(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const email = this.form.value.email?.trim() ?? '';
    const password = this.form.value.password ?? '';

    this.cargando = true;
    this.authSession.login(email, password).subscribe({
      next: () => {
        this.cargando = false;
        this.snackBar.open('Inicio de sesion correcto', 'Cerrar', { duration: 3000 });
        void this.router.navigate([this.getPostLoginRoute()]);
      },
      error: () => {
        this.cargando = false;
        this.snackBar.open('No se pudo iniciar sesion. Verifica tus credenciales.', 'Cerrar', { duration: 3500 });
      }
    });
  }

  private getPostLoginRoute(): string {
    return homeRoute(this.authSession.user);
  }
}
import { homeRoute } from './core/auth/home-route';
