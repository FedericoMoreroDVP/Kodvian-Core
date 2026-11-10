import { Component, HostListener, inject } from '@angular/core';
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

  isMobile = this.detectMobile();
  sidenavOpened = !this.isMobile;

  @HostListener('window:resize')
  onResize(): void {
    this.isMobile = this.detectMobile();
    this.sidenavOpened = !this.isMobile;
  }

  toggleSidebar(): void {
    this.sidenavOpened = !this.sidenavOpened;
  }

  onSidebarItemSelected(): void {
    if (this.isMobile) {
      this.sidenavOpened = false;
    }
  }

  cerrarSesion(): void {
    this.authSession.logout().subscribe(() => {
      void this.router.navigate(['/login']);
    });
  }

  private detectMobile(): boolean {
    return window.matchMedia('(max-width: 960px)').matches;
  }
}
