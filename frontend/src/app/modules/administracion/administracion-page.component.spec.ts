import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthSessionService } from '../../core/auth/auth-session.service';
import { AdministracionPageComponent } from './administracion-page.component';
import { ManagedUser } from './users.service';

describe('Administración de usuarios', () => {
  let http: HttpTestingController;
  const user: ManagedUser = { id: 'self', fullName: 'Socio', email: 'socio@example.test', roles: ['Administrador', 'Desarrollador'], isActive: true, sessionVersion: 'version' };
  let session: { user: { id: string }; clearSession: jasmine.Spy };
  let router: { navigate: jasmine.Spy };
  beforeEach(() => {
    session = { user: { id: 'self' }, clearSession: jasmine.createSpy() };
    router = { navigate: jasmine.createSpy() };
    TestBed.configureTestingModule({ imports: [AdministracionPageComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
      { provide: AuthSessionService, useValue: session }, { provide: Router, useValue: router },
      { provide: MatSnackBar, useValue: { open: jasmine.createSpy() } },
      { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(user) }) } }
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('muestra cuentas, roles y estado, y consulta con búsqueda y paginación', () => {
    const fixture = TestBed.createComponent(AdministracionPageComponent); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/users').flush({ data: { items: [user], totalCount: 21, pageNumber: 1, pageSize: 20 } });
    http.expectOne('/api/users/roles').flush({ data: ['Administrador', 'Analista', 'Desarrollador'] }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Socio'); expect(fixture.nativeElement.textContent).toContain('Desarrollador');
    fixture.componentInstance.search = ' socio '; fixture.componentInstance.filter();
    const filtered = http.expectOne(r => r.url === '/api/users'); expect(filtered.request.params.get('search')).toBe('socio');
    filtered.flush({ data: { items: [user], totalCount: 21 } });
    fixture.componentInstance.page({ pageIndex: 1, pageSize: 20, length: 21 });
    const paged = http.expectOne(r => r.url === '/api/users'); expect(paged.request.params.get('pageNumber')).toBe('2');
    paged.flush({ data: { items: [user], totalCount: 21 } }); fixture.destroy();
  });

  it('vuelve al ingreso cuando el administrador cambia sus propios roles', () => {
    const component = TestBed.runInInjectionContext(() => new AdministracionPageComponent());
    component.roles = ['Administrador', 'Desarrollador']; component.edit(user);
    expect(session.clearSession).toHaveBeenCalled(); expect(router.navigate).toHaveBeenCalledWith(['/login']);
    http.expectNone(r => r.url === '/api/users'); component.ngOnDestroy();
  });
});
