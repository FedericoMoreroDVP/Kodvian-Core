import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AuthSessionService } from '../core/auth/auth-session.service';
import { MainLayoutComponent } from './main-layout.component';

describe('Layout principal', () => {
  beforeEach(() => {
    const session = jasmine.createSpyObj<AuthSessionService>('AuthSessionService', ['hasPermission', 'logout', 'ensureSessionLoaded'], { user: null, user$: of(null) });
    session.ensureSessionLoaded.and.returnValue(of(null));
    session.hasPermission.and.returnValue(of(false));
    TestBed.configureTestingModule({
      imports: [MainLayoutComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), { provide: AuthSessionService, useValue: session }]
    });
  });

  it('el menú arranca oculto, se abre con la hamburguesa y se oculta al elegir una opción', () => {
    const fixture = TestBed.createComponent(MainLayoutComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    expect(component.sidenavOpened).toBeFalse();

    component.toggleSidebar();
    fixture.detectChanges();
    expect(component.sidenavOpened).toBeTrue();

    component.onSidebarItemSelected();
    fixture.detectChanges();
    expect(component.sidenavOpened).toBeFalse();
  });

  it('el botón de menú informa si el menú está abierto', () => {
    const fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('app-header button[aria-controls="main-sidenav"]');
    expect(button.getAttribute('aria-expanded')).toBe('false');

    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.getAttribute('aria-label')).toBe('Cerrar menú');
  });
});
