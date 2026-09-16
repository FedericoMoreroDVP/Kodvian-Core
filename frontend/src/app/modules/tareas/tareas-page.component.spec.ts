import { TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CdkDrag, CdkDragDrop } from '@angular/cdk/drag-drop';
import { By } from '@angular/platform-browser';
import { ApplicationRef } from '@angular/core';
import { of, Subject } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';
import { AuthSessionService } from '../../core/auth/auth-session.service';
import { TareasPageComponent } from './tareas-page.component';
import { TareasService } from './services/tareas.service';
import { KanbanColumn } from './models/tareas.models';

describe('Tablero de tareas: movimientos', () => {
  let component: TareasPageComponent;
  let response: Subject<any>;
  let api: jasmine.SpyObj<TareasService>;
  let source: KanbanColumn;
  let target: KanbanColumn;
  let session: { user: { permissions: string[] } };

  beforeEach(() => {
    response = new Subject();
    api = jasmine.createSpyObj('TareasService', ['actualizarEstado']);
    api.actualizarEstado.and.returnValue(response);
    session = { user: { permissions: ['tasks.write'] } };
    TestBed.configureTestingModule({ providers: [FormBuilder,
      { provide: TareasService, useValue: api },
      { provide: MatDialog, useValue: {} },
      { provide: MatSnackBar, useValue: jasmine.createSpyObj('snack', ['open']) },
      { provide: ActivatedRoute, useValue: {} }, { provide: Router, useValue: {} },
      { provide: AuthSessionService, useValue: session }
    ] });
    component = TestBed.runInInjectionContext(() => new TareasPageComponent());
    spyOn(component, 'cargarDatos');
    source = { status: 'Pendiente', title: '', items: [
      { id: 'task', title: 'Mejorar Planner', projectName: 'Planner', priority: 'Media', kanbanOrder: 9 }
    ] };
    target = { status: 'EnCurso', title: '', items: [] };
  });
  function event(overrides = {}): CdkDragDrop<KanbanColumn> {
    return { previousContainer: { data: source }, container: { data: target },
      item: { data: source.items[0] }, isPointerOverContainer: true, ...overrides } as CdkDragDrop<KanbanColumn>;
  }
  it('inicia en tablero y conserva el orden al mover a una columna vacía', () => {
    expect(component.vista).toBe('kanban');
    component.moverTarjeta(event());
    expect(source.items.length).toBe(0);
    expect(target.items[0].id).toBe('task');
    expect(component.movingId).toBe('task');
    expect(api.actualizarEstado).toHaveBeenCalledWith('task', 'EnCurso', 9);
    response.next({});
    expect(component.movingId).toBeNull();
    expect(component.cargarDatos).toHaveBeenCalled();
  });
  it('restaura la tarjeta y su posición ante un error de guardado', () => {
    component.moverTarjeta(event());
    response.error(new Error('red'));
    expect(source.items[0].id).toBe('task');
    expect(target.items.length).toBe(0);
    expect(component.movingId).toBeNull();
  });
  it('no escribe si se suelta fuera o en la misma columna', () => {
    component.moverTarjeta(event({ isPointerOverContainer: false }));
    const container = { data: source };
    component.moverTarjeta(event({ container, previousContainer: container }));
    expect(api.actualizarEstado).not.toHaveBeenCalled();
  });
  it('no permite mover sin permiso ni mientras otro movimiento está pendiente', () => {
    session.user.permissions = [];
    component.moverTarjeta(event());
    expect(api.actualizarEstado).not.toHaveBeenCalled();
    session.user.permissions = ['tasks.write']; component.movingId = 'other';
    component.moverTarjeta(event());
    expect(api.actualizarEstado).not.toHaveBeenCalled();
  });
});

describe('Tablero: interacción real con CDK', () => {
  it('permite mover inmediatamente con mouse, sin iniciar arrastre desde los botones', async () => {
    const api = jasmine.createSpyObj('api', ['obtenerLookups', 'obtenerKanban', 'actualizarEstado']);
    api.obtenerLookups.and.returnValue(of({ projects: [], developers: [] }));
    const source: KanbanColumn = { status: 'Pendiente', title: '', items: [
      { id: 'task', title: 'Mejorar Planner', projectName: 'Planner', priority: 'Media', kanbanOrder: 9 }
    ] };
    const target: KanbanColumn = { status: 'EnCurso', title: '', items: [] };
    api.obtenerKanban.and.returnValue(of([source, target]));
    api.actualizarEstado.and.returnValue(new Subject());
    TestBed.configureTestingModule({ imports: [TareasPageComponent], providers: [
      provideNoopAnimations(), provideNativeDateAdapter(),
      { provide: TareasService, useValue: api },
      { provide: MatDialog, useValue: {} },
      { provide: MatSnackBar, useValue: jasmine.createSpyObj('snack', ['open']) },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      { provide: Router, useValue: {} },
      { provide: AuthSessionService, useValue: { user: { permissions: ['tasks.write'] } } }
    ] });
    const fixture = TestBed.createComponent(TareasPageComponent);
    fixture.detectChanges(); await fixture.whenStable();
    TestBed.inject(ApplicationRef).tick();
    const card = fixture.nativeElement.querySelector('.kanban-card') as HTMLElement;
    const column = fixture.nativeElement.querySelectorAll('.kanban-col')[1] as HTMLElement;
    const drag = fixture.debugElement.query(By.directive(CdkDrag)).injector.get(CdkDrag);
    expect(drag.disabled).toBeFalse();
    expect(drag.getRootElement()).toBe(card);
    card.scrollIntoView();
    const start = card.getBoundingClientRect();
    const end = column.getBoundingClientRect();
    const x = start.left + 30, y = start.top + 20;
    const send = (element: EventTarget, type: string, clientX: number, clientY: number) =>
      element.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, buttons: 1, detail: 1, clientX, clientY }));
    const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
    for (const button of Array.from(card.querySelectorAll('button'))) {
      send(button, 'mousedown', x, y);
      send(document, 'mousemove', x + 10, y + 10);
      expect(document.querySelector('.cdk-drag-preview')).withContext('Los botones no deben iniciar arrastre').toBeNull();
      send(document, 'mouseup', x + 10, y + 10);
    }
    expect(drag.dragStartDelay).toEqual({ mouse: 0, touch: 300 });
    const title = card.querySelector('strong')!;
    send(title, 'mousedown', x, y);
    send(document, 'mousemove', x + 10, y + 10);
    expect(document.querySelector('.cdk-drag-preview')).withContext('El arrastre con mouse debe iniciarse sin espera').not.toBeNull();
    send(document, 'mousemove', end.left + 40, end.top + 80); await wait(30);
    send(document, 'mouseup', end.left + 40, end.top + 80); await wait(400);
    expect(api.actualizarEstado).toHaveBeenCalledWith('task', 'EnCurso', 9);
    fixture.destroy();
  });
});
