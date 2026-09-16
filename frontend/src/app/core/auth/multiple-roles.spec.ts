import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom, Observable, of } from 'rxjs';
import { AuthSessionService } from './auth-session.service';
import { CurrentUser } from './auth.models';
import { homeRoute } from './home-route';
import { administrationGuard } from '../guards/administration.guard';
import { NavigationService } from '../services/navigation.service';

describe('Navegación con múltiples roles', () => {
  function user(roles: string[], permissions: string[]): CurrentUser {
    return { id: 'user', fullName: 'Persona', email: 'person@example.test', role: roles.join(' + '), roles, permissions, developerId: 'profile' };
  }
  it('prioriza Inicio para administradores y conserva Mi trabajo en el menú', () => {
    const account = user(['Administrador', 'Desarrollador'], ['dashboard.read', 'developer.work.read', 'administration.read']);
    expect(homeRoute(account)).toBe('/dashboard');
    const routes = new NavigationService().getItems(account).map(x => x.route);
    expect(routes).toContain('/mi-trabajo'); expect(routes).toContain('/administracion');
  });
  it('no muestra Administración a Solo lectura aunque tenga el permiso antiguo', () => {
    const account = user(['Solo lectura'], ['administration.read', 'projects.read']);
    expect(new NavigationService().getItems(account).map(x => x.route)).not.toContain('/administracion');
    expect(homeRoute(account)).toBe('/proyectos');
  });
  it('envía al desarrollador a Mi trabajo y al analista a Proyectos', () => {
    expect(homeRoute(user(['Desarrollador'], ['developer.work.read']))).toBe('/mi-trabajo');
    expect(homeRoute(user(['Analista'], ['projects.read']))).toBe('/proyectos');
  });
  it('el guard bloquea Administración para un analista', async () => {
    const account = user(['Analista'], ['projects.read']);
    TestBed.configureTestingModule({ providers: [
      { provide: AuthSessionService, useValue: { ensureSessionLoaded: () => of(account) } },
      { provide: Router, useValue: { createUrlTree: (paths: string[]) => paths.join('') } }
    ] });
    const result = TestBed.runInInjectionContext(() => administrationGuard({} as any, {} as any));
    expect(await firstValueFrom(result as Observable<any>)).toBe('/proyectos');
  });
});
