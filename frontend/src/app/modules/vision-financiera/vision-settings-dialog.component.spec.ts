import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { VisionSettingsDialogComponent } from './vision-settings-dialog.component';
import { FinanceOverview } from '../finanzas/models/finance-overview.models';

describe('Configuración financiera separada', () => {
  let http: HttpTestingController;
  let ref: { close: jasmine.Spy; disableClose: boolean };
  const overview = { setup: { startDate: null, openingArs: null, openingUsd: null, historyComplete: false, version: 'v1' }, currencies: [] } as unknown as FinanceOverview;
  beforeEach(() => {
    ref = { close: jasmine.createSpy(), disableClose: false };
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: ref }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('conserva saldos desconocidos como null y bloquea el cierre durante el guardado', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'setup', overview }));
    void component.save(); component.close(); expect(ref.close).not.toHaveBeenCalled(); expect(ref.disableClose).toBeTrue();
    const request = http.expectOne('/api/finance/setup'); expect(request.request.body.openingArs).toBeNull(); expect(request.request.body.openingUsd).toBeNull();
    request.flush({ data: overview.setup }); tick(); expect(ref.close).toHaveBeenCalledWith(true);
  }));
  it('mantiene el identificador y los importes de un cambio al reintentar', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'exchange', overview }));
    component.exchange.fromAmount = 1000; component.exchange.toAmount = 1; const id = component.exchange.requestId;
    void component.save(); http.expectOne('/api/finance/exchanges').flush({}, { status: 500, statusText: 'Error' }); tick();
    expect(component.exchange.fromAmount).toBe(1000); expect(ref.close).not.toHaveBeenCalled();
    void component.save(); const retry = http.expectOne('/api/finance/exchanges'); expect(retry.request.body.requestId).toBe(id); retry.flush({ data: id }); tick();
    expect(ref.close).toHaveBeenCalledWith(true);
  }));
  it('crea un socio y actualiza el listado dentro de su diálogo', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'partners', overview }));
    component.partnerMode = 'new'; component.partnerName = 'Socio'; component.partnerEmail = 'socio@example.test'; void component.savePartner();
    const request = http.expectOne('/api/finance/partners'); expect(request.request.method).toBe('POST');
    expect(request.request.body.source).toBe('Manual'); expect(request.request.body.email).toBe('socio@example.test');
    request.flush({ data: { id: 'p', fullName: 'Socio' } }); tick();
    http.expectOne('/api/finance/partners').flush({ data: [{ id: 'p', fullName: 'Socio', isActive: true }] }); tick();
    expect(component.partners.length).toBe(1); expect(component.changed).toBeTrue();
  }));
  it('selecciona una persona existente y envía su identidad, sin duplicar los datos maestros', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'partners', overview }));
    component.ngOnInit();
    http.expectOne('/api/finance/partners').flush({ data: [] });
    const person = { source: 'Developer' as const, personId: 'team-person', fullName: 'Persona de Equipo', email: 'team@example.test', registeredPartnerId: null };
    http.expectOne(r => r.url === '/api/finance/partners/people').flush({ data: { items: [person], totalCount: 1 } }); tick();
    component.selectedPersonKey = component.personKey(person); expect(component.canAddPartner).toBeTrue();
    void component.savePartner();
    const request = http.expectOne('/api/finance/partners');
    expect(request.request.body).toEqual({ fullName: '', isActive: true, source: 'Developer', personId: 'team-person' });
    request.flush({ data: { id: 'p', source: 'Developer', personId: 'team-person', fullName: person.fullName } }); tick();
    http.expectOne('/api/finance/partners').flush({ data: [{ id: 'p', source: 'Developer', personId: 'team-person', fullName: person.fullName, isActive: true }] }); tick();
    http.expectOne(r => r.url === '/api/finance/partners/people').flush({ data: { items: [{ ...person, registeredPartnerId: 'p' }], totalCount: 1 } }); tick();
    expect(component.partners[0].personId).toBe('team-person'); expect(component.changed).toBeTrue();
  }));
  it('vincula un socio manual mediante PUT conservando su identificador', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'partners', overview }));
    component.linkExistingPartner({ id: 'old-partner', fullName: 'Nombre anterior', source: 'Manual', isActive: true });
    const person = { source: 'User' as const, personId: 'account', fullName: 'Persona', registeredPartnerId: null };
    http.expectOne(r => r.url === '/api/finance/partners/people').flush({ data: { items: [person], totalCount: 1 } }); tick();
    component.selectedPersonKey = component.personKey(person); void component.savePartner();
    const request = http.expectOne('/api/finance/partners/old-partner'); expect(request.request.method).toBe('PUT');
    expect(request.request.body.personId).toBe('account');
    request.flush({ data: { id: 'old-partner' } }); tick();
    http.expectOne('/api/finance/partners').flush({ data: [{ id: 'old-partner', fullName: 'Persona', isActive: true }] }); tick();
    http.expectOne(r => r.url === '/api/finance/partners/people').flush({ data: { items: [{ ...person, registeredPartnerId: 'old-partner' }], totalCount: 1 } }); tick();
    expect(component.partners[0].id).toBe('old-partner');
  }));
  it('bloquea personas ya registradas y conserva la selección cuando falla el servidor', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'partners', overview }));
    const person = { source: 'Developer' as const, personId: 'person', fullName: 'Persona', registeredPartnerId: 'existing' as string | null };
    component.people = [person]; component.selectedPersonKey = component.personKey(person);
    expect(component.canAddPartner).toBeFalse(); void component.savePartner(); http.expectNone('/api/finance/partners');
    person.registeredPartnerId = null;
    void component.savePartner(); http.expectOne('/api/finance/partners').flush({ message: 'No disponible' }, { status: 500, statusText: 'Error' }); tick();
    expect(component.selectedPersonKey).toBe('Developer:person'); expect(component.error).toBe('No disponible'); expect(component.saving).toBeFalse();
  }));
  it('envía búsqueda y paginación al consultar las personas disponibles', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'partners', overview }));
    component.peopleSearch = ' socio@example.test '; void component.loadPeople(2);
    const request = http.expectOne(r => r.url === '/api/finance/partners/people');
    expect(request.request.params.get('search')).toBe('socio@example.test'); expect(request.request.params.get('pageNumber')).toBe('2');
    request.flush({ data: { items: [], totalCount: 21 } }); tick(); expect(component.peoplePage).toBe(2);
  }));
  it('sugiere vincular fichas manuales similares sin asociarlas automáticamente', () => {
    const component = TestBed.runInInjectionContext(() => new VisionSettingsDialogComponent({ mode: 'partners', overview }));
    component.partners = [{ id: 'old', fullName: 'Nicolas', isActive: true, source: 'Manual' }];
    component.people = [{ source: 'Developer', personId: 'team', fullName: 'Nicolás' }];
    component.selectedPersonKey = 'Developer:team';
    expect(component.similarManualPartners.map(p => p.id)).toEqual(['old']);
    expect(component.linkingPartner).toBeUndefined(); http.expectNone(() => true);
  });
});
