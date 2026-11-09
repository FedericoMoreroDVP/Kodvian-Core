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
    fixture.destroy();
  });
});
