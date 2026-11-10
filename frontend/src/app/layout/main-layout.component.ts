import { Component, inject } from '@angular/core';
import { MatSidenavModule } from '@angular/material/sidenav';
import { Router, RouterOutlet } from '@angular/router';

import { AuthSessionService } from '../core/auth/auth-session.service';
import { HeaderComponent } from './header.component';
import { SidebarComponent } from './sidebar.component';
import { AiQuickLauncherComponent } from './ai-quick-launcher.component';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [MatSidenavModule, RouterOutlet, HeaderComponent, SidebarComponent, AiQuickLauncherComponent],
  templateUrl: './main-layout.component.html',
  styleUrl: './main-layout.component.scss'
})
export class MainLayoutComponent {
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);

  /** El menú arranca oculto y queda oculto entre navegaciones: se abre solo con la hamburguesa. */
  sidenavOpened = false;

  toggleSidebar(): void {
    this.sidenavOpened = !this.sidenavOpened;
  }

  onOpenedChange(opened: boolean): void {
    this.sidenavOpened = opened;
  }

  onSidebarItemSelected(): void {
    this.sidenavOpened = false;
  }

  cerrarSesion(): void {
    this.authSession.logout().subscribe(() => {
      void this.router.navigate(['/login']);
    });
  }
}
