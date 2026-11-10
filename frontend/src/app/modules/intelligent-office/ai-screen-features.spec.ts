import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, EMPTY, of, throwError } from 'rxjs';

import { AuthSessionService } from '../../core/auth/auth-session.service';
import { AiQuickLauncherComponent } from '../../layout/ai-quick-launcher.component';
import { AiMessageExtrasComponent } from './ai-message-extras.component';
import { IntelligentOfficePageComponent } from './intelligent-office-page.component';
import { AiMessage } from './intelligent-office.models';
import { IntelligentOfficeService, StreamUnavailableError } from './intelligent-office.service';

const conversation = { id: 'c1', title: 'Finanzas', createdAtUtc: '2026-11-10T12:00:00Z', updatedAtUtc: '2026-11-10T12:00:00Z', messageCount: 2, isPinned: false };
const assistant: AiMessage = {
  id: 'm2', role: 'assistant', content: 'Resumen de noviembre.', createdAtUtc: '2026-11-10T12:00:02Z', traceId: null, sources: [],
  references: [{ kind: 'Proyecto', name: 'Portal', id: 'p1' }, { kind: 'Tarea', name: 'Informe', id: 't1' }],
  visual: { title: 'Ingresos y gastos del período', groups: [{ currency: 'ARS', labels: ['Ingresos', 'Gastos'], datasets: [{ label: 'noviembre', values: [1000, 400] }] }] },
  feedback: null
};

describe('Oficina Inteligente: pantalla', () => {
  let http: HttpTestingController;

  function setup(queryParams: Record<string, string> = {}): void {
    TestBed.configureTestingModule({
      imports: [IntelligentOfficePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } }]
    });
    http = TestBed.inject(HttpTestingController);
    const api = TestBed.inject(IntelligentOfficeService);
    spyOn(api, 'sendStream').and.returnValue(throwError(() => new StreamUnavailableError()));
    spyOn(api, 'briefing').and.returnValue(EMPTY);
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  }

  afterEach(() => http.verify());

  it('muestra tarjetas de resultados y gráfico, y guarda la valoración', () => {
    setup();
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [conversation], pageNumber: 1, pageSize: 50, totalCount: 1 } });
    http.expectOne('/api/ai/conversations/c1').flush({ data: { ...conversation, messages: [
      { id: 'm1', role: 'user', content: '¿Cómo vamos?', createdAtUtc: '2026-11-10T12:00:00Z', traceId: null, sources: [] }, assistant
    ] } });
    fixture.detectChanges();

    const results = fixture.nativeElement.querySelectorAll('.result');
    expect(results.length).toBe(2);
    expect(results[0].getAttribute('href')).toBe('/proyectos');
    expect(fixture.nativeElement.querySelector('app-finance-chart')).not.toBeNull();

    (fixture.nativeElement.querySelector('.feedback-up') as HTMLButtonElement).click();
    const feedback = http.expectOne('/api/ai/conversations/c1/messages/m2/feedback');
    expect(feedback.request.body).toEqual({ value: 1 });
    feedback.flush({ data: { ...assistant, feedback: 1 } });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.feedback-up').classList).toContain('active');
    fixture.destroy();
  });

  it('fija, renombra y busca conversaciones', async () => {
    setup();
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [conversation], pageNumber: 1, pageSize: 50, totalCount: 1 } });
    http.expectOne('/api/ai/conversations/c1').flush({ data: { ...conversation, messages: [] } });
    fixture.detectChanges();

    // La lista de conversaciones se puede ocultar; en este test se abre explícitamente.
    if (!fixture.componentInstance.showHistory) fixture.componentInstance.toggleHistory();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-ai-conversation-list')).not.toBeNull();
    (fixture.nativeElement.querySelector('.pin-button') as HTMLButtonElement).click();
    const pin = http.expectOne(req => req.method === 'PATCH' && req.url === '/api/ai/conversations/c1');
    expect(pin.request.body).toEqual({ isPinned: true });
    pin.flush({ data: { ...conversation, isPinned: true } });

    const component = fixture.componentInstance;
    component.startRename();
    component.titleDraft = 'Cierre de noviembre';
    component.saveTitle();
    const rename = http.expectOne(req => req.method === 'PATCH' && req.url === '/api/ai/conversations/c1');
    expect(rename.request.body).toEqual({ title: 'Cierre de noviembre' });
    rename.flush({ data: { ...conversation, title: 'Cierre de noviembre', isPinned: true } });
    expect(component.activeConversation?.title).toBe('Cierre de noviembre');

    component.search = 'acme';
    component.onSearchChange();
    await new Promise(resolve => setTimeout(resolve, 350));
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50&search=acme').flush({ data: { items: [], pageNumber: 1, pageSize: 50, totalCount: 0 } });
    expect(component.conversations.length).toBe(0);
    fixture.destroy();
  });

  it('una pregunta rápida (Ctrl+K) abre una conversación nueva con la pantalla de origen', () => {
    setup({ q: '¿Cómo viene este proyecto?', ctx: '/proyectos' });
    const fixture = TestBed.createComponent(IntelligentOfficePageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/conversations?pageNumber=1&pageSize=50').flush({ data: { items: [conversation], pageNumber: 1, pageSize: 50, totalCount: 1 } });
    http.expectOne('/api/ai/conversations').flush({ data: { ...conversation, id: 'c2', title: 'Nueva conversación', messageCount: 0 } });

    const sent = http.expectOne('/api/ai/conversations/c2/messages');
    expect(sent.request.body.content).toBe('¿Cómo viene este proyecto?');
    expect(sent.request.body.pageContext).toBe('/proyectos');
    sent.flush({ message: 'x' }, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/ai/conversations/c2').flush({ data: { ...conversation, id: 'c2', messages: [] } });
    fixture.destroy();
  });
});

describe('Complementos de mensaje', () => {
  it('arma un gráfico por moneda sin mezclar ARS y USD', () => {
    TestBed.configureTestingModule({ imports: [AiMessageExtrasComponent], providers: [provideRouter([]), provideNoopAnimations()] });
    const fixture = TestBed.createComponent(AiMessageExtrasComponent);
    fixture.componentInstance.message = {
      ...assistant,
      visual: { title: 'oct vs nov', groups: [
        { currency: 'ARS', labels: ['Ingresos', 'Gastos'], datasets: [{ label: 'oct', values: [1, 2] }, { label: 'nov', values: [3, 4] }] },
        { currency: 'USD', labels: ['Ingresos', 'Gastos'], datasets: [{ label: 'oct', values: [5, 6] }, { label: 'nov', values: [7, 8] }] }
      ] }
    };
    fixture.componentInstance.ngOnChanges();
    expect(fixture.componentInstance.charts.map(x => x.currency)).toEqual(['ARS', 'USD']);
    expect(fixture.componentInstance.charts[1].series[1].values).toEqual([7, 8]);
  });
});

describe('Lanzador rápido', () => {
  it('Ctrl+K abre el lanzador y Enter navega a Oficina Inteligente con la pantalla actual', () => {
    const user = new BehaviorSubject(true);
    TestBed.configureTestingModule({
      imports: [AiQuickLauncherComponent],
      providers: [provideRouter([]), provideNoopAnimations(), { provide: AuthSessionService, useValue: { hasPermission: () => user.asObservable() } }]
    });
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(AiQuickLauncherComponent);
    spyOn(fixture.componentInstance, 'currentPath').and.returnValue('/tareas');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.fab')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.launcher')).not.toBeNull();

    fixture.componentInstance.question = '¿Qué vence hoy?';
    fixture.componentInstance.submit();
    expect(navigate).toHaveBeenCalledWith(['/oficina-inteligente'], { queryParams: { q: '¿Qué vence hoy?', ctx: '/tareas' } });
    fixture.destroy();
  });

  it('no se muestra sin permiso de Oficina Inteligente', () => {
    TestBed.configureTestingModule({
      imports: [AiQuickLauncherComponent],
      providers: [provideRouter([]), provideNoopAnimations(), { provide: AuthSessionService, useValue: { hasPermission: () => of(false) } }]
    });
    const fixture = TestBed.createComponent(AiQuickLauncherComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.fab')).toBeNull();
  });
});
