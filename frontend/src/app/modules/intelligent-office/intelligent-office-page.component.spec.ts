import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';

import { EMPTY, of, Subject, throwError } from 'rxjs';

import { IntelligentOfficePageComponent } from './intelligent-office-page.component';
import { AiStreamEvent } from './intelligent-office.models';
import { IntelligentOfficeService, StreamUnavailableError } from './intelligent-office.service';

describe('Oficina Inteligente', () => {
  let http: HttpTestingController;
  let streamSpy: jasmine.Spy;
  let briefingSpy: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IntelligentOfficePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
    // Por defecto el streaming "no está disponible" y la pantalla usa el envío común (HttpClient).
    streamSpy = spyOn(TestBed.inject(IntelligentOfficeService), 'sendStream').and.returnValue(throwError(() => new StreamUnavailableError()));
    briefingSpy = spyOn(TestBed.inject(IntelligentOfficeService), 'briefing').and.returnValue(EMPTY);
  });

  it('muestra el progreso y el texto en vivo y después las sugerencias de seguimiento', () => {
    const events = new Subject<AiStreamEvent>();
    streamSpy.and.returnValue(events.asObservable());
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [], pageNumber: 1, pageSize: 50, totalCount: 0 } });

    fixture.componentInstance.draft = '¿Qué vence esta semana?';
    fixture.componentInstance.send();
    http.expectOne('/api/ai/conversations').flush({ data: { id: 'c1', title: 'Nueva conversación', createdAtUtc: '2026-11-10T12:00:00Z', updatedAtUtc: '2026-11-10T12:00:00Z', messageCount: 0 } });

    events.next({ type: 'status', data: { status: 'tool', label: 'Consultando tareas…', toolName: 'tasks.list' } });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.progress-label').textContent).toContain('Consultando tareas…');

    events.next({ type: 'delta', data: { text: 'Vencen **3** tareas.' } });
    events.next({ type: 'delta', data: { text: '\n[[sugerencias: Ver detalle' } });
    fixture.detectChanges();
    const live: HTMLElement = fixture.nativeElement.querySelector('.streaming');
    expect(live.textContent).toContain('Vencen 3 tareas.');
    expect(live.textContent).not.toContain('sugerencias');

    events.next({ type: 'done', data: {
      runId: 'r1', traceId: 't1',
      userMessage: { id: 'u1', role: 'user', content: '¿Qué vence esta semana?', createdAtUtc: '2026-11-10T12:00:00Z', traceId: 't1', sources: [] },
      assistantMessage: { id: 'a1', role: 'assistant', content: 'Vencen **3** tareas.', createdAtUtc: '2026-11-10T12:00:03Z', traceId: 't1', sources: [], suggestions: ['Ver detalle', 'Pasar a finalizada'] }
    } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.streaming')).toBeNull();
    const followups = fixture.nativeElement.querySelectorAll('.followup');
    expect(followups.length).toBe(2);
    expect(followups[0].textContent).toContain('Ver detalle');
    expect(fixture.componentInstance.busy).toBeFalse();
    fixture.destroy();
  });

  afterEach(() => http.verify());

  it('inicia una conversación al primer mensaje y muestra fuentes como texto seguro', () => {
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [], pageNumber: 1, pageSize: 50, totalCount: 0 } });

    fixture.componentInstance.draft = '¿Qué tareas están vencidas?';
    fixture.componentInstance.send();
    const created = http.expectOne('/api/ai/conversations');
    expect(created.request.method).toBe('POST');
    created.flush({ data: { id: 'conversation-1', title: 'Nueva conversación', createdAtUtc: '2026-11-07T12:00:00Z', updatedAtUtc: '2026-11-07T12:00:00Z', messageCount: 0 } });

    const sent = http.expectOne('/api/ai/conversations/conversation-1/messages');
    expect(sent.request.body.content).toBe('¿Qué tareas están vencidas?');
    sent.flush({ data: {
      runId: 'run-1', traceId: 'trace-1',
      userMessage: { id: 'message-1', role: 'user', content: '¿Qué tareas están vencidas?', createdAtUtc: '2026-11-07T12:00:00Z', traceId: 'trace-1', sources: [] },
      assistantMessage: {
        id: 'message-2', role: 'assistant', content: 'Encontré tareas. <script>alert(1)</script>', createdAtUtc: '2026-11-07T12:00:02Z', traceId: 'trace-1',
        sources: [{ toolName: 'tasks.list', source: 'Kodvian.Core.Application.Tasks', asOfUtc: '2026-11-07T12:00:00Z', from: '2026-10-09', to: '2026-11-07', currency: null, link: '/tareas' }]
      }
    } });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Encontré tareas. <script>alert(1)</script>');
    expect(fixture.nativeElement.querySelector('script')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/tareas"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/tareas"]').textContent).toContain('Tareas');
    fixture.destroy();
  });

  it('envía una sugerencia y muestra la consulta mientras espera la respuesta', () => {
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [], pageNumber: 1, pageSize: 50, totalCount: 0 } });
    fixture.detectChanges();

    const suggestion: HTMLButtonElement = fixture.nativeElement.querySelector('.suggestion');
    suggestion.click();
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations').flush({ data: { id: 'conversation-1', title: 'Nueva conversación', createdAtUtc: '2026-11-07T12:00:00Z', updatedAtUtc: '2026-11-07T12:00:00Z', messageCount: 0 } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.pending').textContent).toContain(fixture.componentInstance.suggestions[0].text);
    expect(fixture.nativeElement.querySelector('.thinking')).not.toBeNull();
    const sent = http.expectOne('/api/ai/conversations/conversation-1/messages');
    sent.flush({ message: 'El proveedor no está disponible.' }, { status: 503, statusText: 'Service Unavailable' });
    http.expectOne('/api/ai/conversations/conversation-1').flush({ data: { id: 'conversation-1', title: 'Nueva conversación', createdAtUtc: '2026-11-07T12:00:00Z', updatedAtUtc: '2026-11-07T12:00:00Z', messageCount: 0, messages: [] } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.pending')).toBeNull();
    expect(fixture.componentInstance.draft).toBe(fixture.componentInstance.suggestions[0].text);
    fixture.destroy();
  });

  it('muestra la acción propuesta y al confirmarla agrega el resultado', () => {
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    const conversation = { id: 'conversation-1', title: 'Agendar', createdAtUtc: '2026-11-10T12:00:00Z', updatedAtUtc: '2026-11-10T12:00:00Z', messageCount: 2 };
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [conversation], pageNumber: 1, pageSize: 50, totalCount: 1 } });
    const action = {
      id: 'action-1', toolName: 'tasks.create', title: 'Crear tarea', summary: 'Crear la tarea «Llamar a Juan» en Proyecto Demo.',
      details: [{ label: 'Proyecto', value: 'Proyecto Demo' }], status: 'Pending', expiresAtUtc: new Date(Date.now() + 600000).toISOString(),
      resolvedAtUtc: null, resultMessage: null, resultLink: null
    };
    http.expectOne('/api/ai/conversations/conversation-1').flush({ data: { ...conversation, messages: [
      { id: 'm1', role: 'user', content: 'Agendame llamar a Juan', createdAtUtc: '2026-11-10T12:00:00Z', traceId: null, sources: [] },
      { id: 'm2', role: 'assistant', content: 'Te dejé la tarea **lista** para confirmar.', createdAtUtc: '2026-11-10T12:00:02Z', traceId: null, sources: [], grounded: true, pendingActions: [action] }
    ] } });
    fixture.detectChanges();

    const card: HTMLElement = fixture.nativeElement.querySelector('.action-card');
    expect(card.textContent).toContain('Llamar a Juan');
    expect(card.textContent).toContain('Proyecto Demo');
    expect(fixture.nativeElement.querySelector('.markdown strong').textContent).toBe('lista');

    (card.querySelector('.confirm-action') as HTMLButtonElement).click();
    const confirm = http.expectOne('/api/ai/conversations/conversation-1/actions/action-1/confirm');
    expect(confirm.request.method).toBe('POST');
    confirm.flush({ data: {
      action: { ...action, status: 'Confirmed', resultMessage: 'Listo, creé la tarea «Llamar a Juan» en Proyecto Demo.', resultLink: '/tareas' },
      message: { id: 'm3', role: 'assistant', content: 'Listo, creé la tarea «Llamar a Juan» en Proyecto Demo.', createdAtUtc: '2026-11-10T12:01:00Z', traceId: null, sources: [], grounded: true }
    } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.confirm-action')).toBeNull();
    expect(fixture.nativeElement.querySelector('.action-card').textContent).toContain('Confirmada');
    expect(fixture.componentInstance.messages.length).toBe(3);
    fixture.destroy();
  });

  it('confirma varias acciones juntas y permite deshacer una confirmada', () => {
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    const conversation = { id: 'c1', title: 'Tareas', createdAtUtc: '2026-11-10T12:00:00Z', updatedAtUtc: '2026-11-10T12:00:00Z', messageCount: 2 };
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [conversation], pageNumber: 1, pageSize: 50, totalCount: 1 } });
    const expires = new Date(Date.now() + 600000).toISOString();
    const pending = (id: string, title: string) => ({ id, toolName: 'tasks.create', title: 'Crear tarea', summary: title, details: [], status: 'Pending', expiresAtUtc: expires, resolvedAtUtc: null, resultMessage: null, resultLink: null });
    http.expectOne('/api/ai/conversations/c1').flush({ data: { ...conversation, messages: [
      { id: 'm1', role: 'user', content: 'Creame dos tareas', createdAtUtc: '2026-11-10T12:00:00Z', traceId: null, sources: [] },
      { id: 'm2', role: 'assistant', content: 'Listas para confirmar.', createdAtUtc: '2026-11-10T12:00:02Z', traceId: null, sources: [], pendingActions: [pending('a1', 'Tarea A'), pending('a2', 'Tarea B')] }
    ] } });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.confirm-all') as HTMLButtonElement).click();
    const batch = http.expectOne('/api/ai/conversations/c1/messages/m2/actions/confirm-all');
    const undoUntil = new Date(Date.now() + 900000).toISOString();
    batch.flush({ data: {
      actions: [
        { ...pending('a1', 'Tarea A'), status: 'Confirmed', resultMessage: 'Listo A', canUndo: true, undoAvailableUntilUtc: undoUntil },
        { ...pending('a2', 'Tarea B'), status: 'Confirmed', resultMessage: 'Listo B', canUndo: true, undoAvailableUntilUtc: undoUntil }
      ],
      message: { id: 'm3', role: 'assistant', content: 'Listo, confirmé las 2 acciones.', createdAtUtc: '2026-11-10T12:01:00Z', traceId: null, sources: [] }
    } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.confirm-all')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.undo-action').length).toBe(2);

    (fixture.nativeElement.querySelector('.undo-action') as HTMLButtonElement).click();
    http.expectOne('/api/ai/conversations/c1/actions/a1/undo').flush({ data: {
      action: { ...pending('a1', 'Tarea A'), status: 'Undone', resultMessage: 'Listo A', canUndo: false },
      message: { id: 'm4', role: 'assistant', content: 'Deshice la creación de «Tarea A».', createdAtUtc: '2026-11-10T12:02:00Z', traceId: null, sources: [] }
    } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.undo-action').length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Deshecha');
    expect(fixture.componentInstance.messages.length).toBe(4);
    fixture.destroy();
  });

  it('muestra el resumen con alertas y "Resolver con IA" envía la consulta sugerida', () => {
    briefingSpy.and.returnValue(of({
      date: '2026-11-10', greeting: 'Buen día, Nicolás',
      tasks: { overdue: 3, dueToday: 1, dueThisWeek: 4, topOverdue: [{ id: 't1', title: 'Informe', detail: 'vencida hace 9 días' }] },
      meetings: { today: 2, thisWeek: 5, next: [] },
      collections: [{ currency: 'ARS', pending: 150000, overdue: 50000, count: 3 }],
      projectsAtRisk: [],
      alerts: [{ kind: 'overdue_tasks', severity: 'warning', title: '1 tarea vencida hace más de una semana', detail: 'Conviene reprogramarlas.', prompt: 'Mostrame las tareas vencidas hace más de 7 días', link: '/tareas' }]
    }));
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [], pageNumber: 1, pageSize: 50, totalCount: 0 } });
    fixture.detectChanges();

    const briefing: HTMLElement = fixture.nativeElement.querySelector('app-ai-briefing');
    expect(briefing.textContent).toContain('Buen día, Nicolás');
    expect(briefing.textContent).toContain('1 tarea vencida hace más de una semana');
    expect(briefing.textContent).toContain('Informe');

    (briefing.querySelector('.alert-ask') as HTMLButtonElement).click();
    http.expectOne('/api/ai/conversations').flush({ data: { id: 'c1', title: 'Nueva conversación', createdAtUtc: '2026-11-10T12:00:00Z', updatedAtUtc: '2026-11-10T12:00:00Z', messageCount: 0 } });
    const sent = http.expectOne('/api/ai/conversations/c1/messages');
    expect(sent.request.body.content).toBe('Mostrame las tareas vencidas hace más de 7 días');
    sent.flush({ message: 'x' }, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/ai/conversations/c1').flush({ data: { id: 'c1', title: 'x', createdAtUtc: '', updatedAtUtc: '', messageCount: 0, messages: [] } });
    fixture.destroy();
  });

  it('una sugerencia de acción se copia al cuadro de texto sin enviarse', () => {
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [], pageNumber: 1, pageSize: 50, totalCount: 0 } });
    const actionSuggestion = fixture.componentInstance.suggestions.find(x => x.prefill)!;

    fixture.componentInstance.useSuggestion(actionSuggestion);

    expect(fixture.componentInstance.draft).toBe(actionSuggestion.text);
    http.expectNone('/api/ai/conversations');
    fixture.destroy();
  });

  it('Enter envía y Shift + Enter no', () => {
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    const component = fixture.componentInstance;
    spyOn(component, 'send');
    component.onComposerKeydown(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));
    expect(component.send).not.toHaveBeenCalled();
    component.onComposerKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(component.send).toHaveBeenCalledTimes(1);
  });
});
