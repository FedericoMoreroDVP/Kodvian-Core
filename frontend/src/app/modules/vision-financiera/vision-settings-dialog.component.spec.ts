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
    component.partnerName = 'Socio'; void component.savePartner();
    const request = http.expectOne('/api/finance/partners'); expect(request.request.method).toBe('POST'); request.flush({ data: { id: 'p', fullName: 'Socio' } }); tick();
    http.expectOne('/api/finance/partners').flush({ data: [{ id: 'p', fullName: 'Socio', isActive: true }] }); tick();
    expect(component.partners.length).toBe(1); expect(component.changed).toBeTrue();
  }));
});
