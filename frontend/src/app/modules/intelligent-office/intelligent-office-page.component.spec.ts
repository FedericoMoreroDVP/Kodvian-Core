import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';

import { IntelligentOfficePageComponent } from './intelligent-office-page.component';

describe('Oficina Inteligente', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IntelligentOfficePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
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
