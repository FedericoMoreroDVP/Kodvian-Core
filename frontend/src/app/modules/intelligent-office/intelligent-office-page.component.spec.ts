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
