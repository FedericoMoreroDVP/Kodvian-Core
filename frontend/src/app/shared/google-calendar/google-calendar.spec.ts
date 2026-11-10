import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';

import { GoogleCalendarConnectComponent } from './google-calendar-connect.component';
import { GoogleSyncBadgeComponent } from './google-sync-badge.component';

const STATUS = '/api/integrations/google-calendar/status';

describe('Google Calendar', () => {
  let http: HttpTestingController;
  let queryParams: Record<string, string>;

  beforeEach(() => {
    queryParams = {};
    TestBed.configureTestingModule({
      imports: [GoogleCalendarConnectComponent, GoogleSyncBadgeComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
        { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(queryParams) } }) }
      ]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('avisa cuando el servidor no tiene configurada la integración', () => {
    const fixture = TestBed.createComponent(GoogleCalendarConnectComponent);
    fixture.detectChanges();
    http.expectOne(STATUS).flush({ data: { enabled: false, connected: false, email: null, connectedAtUtc: null, lastSyncAtUtc: null, lastError: null } });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('no está configurado');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });

  it('pide la URL de autorización al conectar', () => {
    const fixture = TestBed.createComponent(GoogleCalendarConnectComponent);
    fixture.detectChanges();
    http.expectOne(STATUS).flush({ data: { enabled: true, connected: false, email: null, connectedAtUtc: null, lastSyncAtUtc: null, lastError: null } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Conectar Google Calendar');

    fixture.componentInstance.connect();
    const request = http.expectOne('/api/integrations/google-calendar/connect');
    expect(request.request.method).toBe('POST');
    // No se completa la respuesta para no navegar fuera de la prueba.
    request.flush({ message: 'No configurado' }, { status: 409, statusText: 'Conflict' });
    expect(fixture.componentInstance.messageIsError).toBeTrue();
  });

  it('desconecta la cuenta conectada', () => {
    spyOn(globalThis, 'confirm').and.returnValue(true);
    const fixture = TestBed.createComponent(GoogleCalendarConnectComponent);
    fixture.detectChanges();
    http.expectOne(STATUS).flush({ data: { enabled: true, connected: true, email: 'nico@gmail.com', connectedAtUtc: null, lastSyncAtUtc: null, lastError: null } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('nico@gmail.com');

    fixture.componentInstance.disconnect();
    const request = http.expectOne('/api/integrations/google-calendar');
    expect(request.request.method).toBe('DELETE');
    request.flush({ data: {} });
    http.expectOne(STATUS).flush({ data: { enabled: true, connected: false, email: null, connectedAtUtc: null, lastSyncAtUtc: null, lastError: null } });
    expect(fixture.componentInstance.status?.connected).toBeFalse();
  });

  it('muestra el resultado de volver de Google y limpia la URL', () => {
    queryParams = { googleCalendar: 'error', reason: 'denied' };
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(GoogleCalendarConnectComponent);
    fixture.componentInstance.handleCallback = true;
    fixture.detectChanges();
    http.expectOne(STATUS).flush({ data: { enabled: true, connected: false, email: null, connectedAtUtc: null, lastSyncAtUtc: null, lastError: null } });

    expect(fixture.componentInstance.message).toContain('se canceló el permiso');
    expect(navigate).toHaveBeenCalled();
  });

  it('reintenta la sincronización de una reunión que falló', () => {
    const fixture = TestBed.createComponent(GoogleSyncBadgeComponent);
    fixture.componentRef.setInput('meetingId', 'm-1');
    fixture.componentRef.setInput('status', 'Failed');
    fixture.componentRef.setInput('error', 'Google no respondió');
    let emitted = false;
    fixture.componentInstance.synced.subscribe(() => emitted = true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No sincronizada');

    (fixture.nativeElement.querySelector('button.retry') as HTMLButtonElement).click();
    http.expectOne('/api/integrations/google-calendar/meetings/m-1/sync').flush({ data: { status: 'Synced', error: null } });
    fixture.detectChanges();

    expect(emitted).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('En Google Calendar');
  });
});
