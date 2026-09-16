import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { UserRolesDialogComponent } from './user-roles-dialog.component';
import { ManagedUser } from './users.service';

describe('Modificar roles', () => {
  let http: HttpTestingController;
  let ref: { close: jasmine.Spy; disableClose: boolean };
  const user: ManagedUser = { id: 'user', fullName: 'Persona', email: 'persona@example.test', roles: ['Desarrollador'], isActive: true, sessionVersion: 'version' };
  beforeEach(() => {
    ref = { close: jasmine.createSpy('close'), disableClose: false };
    TestBed.configureTestingModule({ imports: [UserRolesDialogComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
      { provide: MAT_DIALOG_DATA, useValue: { user, roles: ['Administrador', 'Analista', 'Desarrollador', 'Operativo', 'Solo lectura'] } },
      { provide: MatDialogRef, useValue: ref }
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('permite combinar tres roles y envía la versión de la cuenta', () => {
    const fixture = TestBed.createComponent(UserRolesDialogComponent); fixture.detectChanges();
    const component = fixture.componentInstance;
    component.toggle('Administrador', true); component.toggle('Analista', true); component.save();
    const request = http.expectOne('/api/users/user/roles');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ roles: ['Desarrollador', 'Administrador', 'Analista'], expectedVersion: 'version' });
    expect(ref.disableClose).toBeTrue();
    request.flush({ data: { ...user, roles: component.selected } });
    expect(ref.close).toHaveBeenCalled();
    expect(user.roles).toEqual(['Desarrollador']);
  });

  it('bloquea combinaciones con Solo lectura y selecciones vacías', () => {
    const component = TestBed.createComponent(UserRolesDialogComponent).componentInstance;
    component.toggle('Solo lectura', true); expect(component.valid).toBeFalse(); component.save();
    http.expectNone('/api/users/user/roles');
    component.toggle('Desarrollador', false); expect(component.valid).toBeTrue();
    component.toggle('Solo lectura', false); expect(component.valid).toBeFalse();
  });

  it('conserva la selección ante un conflicto o rechazo del servidor', () => {
    const component = TestBed.createComponent(UserRolesDialogComponent).componentInstance;
    component.toggle('Administrador', true); component.save();
    http.expectOne('/api/users/user/roles').flush({ message: 'La cuenta cambió' }, { status: 400, statusText: 'Bad Request' });
    expect(ref.close).not.toHaveBeenCalled(); expect(component.saving).toBeFalse(); expect(ref.disableClose).toBeFalse();
    expect(component.selected).toContain('Administrador'); expect(component.error).toBe('La cuenta cambió');
  });
});
