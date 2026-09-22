import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { FinanceOverviewComponent } from './finance-overview.component';
import { FinanceOverview } from '../models/finance-overview.models';

describe('Situación financiera', () => {
  let http: HttpTestingController;
  const overview = (): FinanceOverview => ({
    setup: { startDate: null, openingArs: null, openingUsd: null, historyComplete: false, version: '00000000-0000-0000-0000-000000000000' },
    from: null, to: '2025-02-28', estimatedDateCount: 1, unclassifiedPayments: 1, months: [], expenses: [], partners: [],
    currencies: ['ARS', 'USD'].map(currency => ({ currency, income: currency === 'ARS' ? 1000 : 10, expense: 0, result: currency === 'ARS' ? 1000 : 10,
      contributions: 0, withdrawals: 0, reimbursements: 0, pendingIncome: 0, pendingExpense: 0, cashChange: 0, recordedCashBalance: currency === 'ARS' ? 1000 : 10, balance: null }))
  });
  beforeEach(() => { TestBed.configureTestingModule({ imports: [FinanceOverviewComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()] }); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('inicia con histórico y muestra monedas separadas y punto de partida desconocido', () => {
    const fixture = TestBed.createComponent(FinanceOverviewComponent); fixture.detectChanges();
    const request = http.expectOne('/api/finance/overview'); expect(request.request.params.keys()).toEqual([]);
    request.flush({ data: overview() }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.currency').length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('Historial en carga');
    expect(fixture.nativeElement.textContent).toContain('Saldo neto registrado al corte');
    expect(fixture.nativeElement.textContent).toContain('Saldo anterior no informado');
    expect(fixture.nativeElement.textContent).toContain('pagos del equipo requieren revisar');
    fixture.destroy();
  });
  it('consulta el período seleccionado y filtra solo la presentación por moneda', () => {
    const fixture = TestBed.createComponent(FinanceOverviewComponent); fixture.detectChanges(); http.expectOne('/api/finance/overview').flush({ data: overview() });
    fixture.componentInstance.from = '2025-01-01'; fixture.componentInstance.to = '2025-02-28'; fixture.componentInstance.reload();
    const request = http.expectOne(r => r.url === '/api/finance/overview'); expect(request.request.params.get('from')).toBe('2025-01-01');
    request.flush({ data: overview() }); fixture.componentInstance.currency = 'USD'; fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.currency').length).toBe(1); fixture.destroy();
  });
  it('guarda saldos desconocidos como null, sin convertirlos a cero', fakeAsync(() => {
    const fixture = TestBed.createComponent(FinanceOverviewComponent); fixture.detectChanges(); http.expectOne('/api/finance/overview').flush({ data: overview() });
    fixture.componentInstance.openSettings(); void fixture.componentInstance.saveSetup();
    const request = http.expectOne('/api/finance/setup'); expect(request.request.body.openingArs).toBeNull(); expect(request.request.body.openingUsd).toBeNull();
    request.flush({ data: overview().setup }); tick(); http.expectOne('/api/finance/overview').flush({ data: overview() }); fixture.destroy();
  }));
  it('conserva el identificador del cambio de moneda si falla y permite reintentar', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new FinanceOverviewComponent());
    component.exchange.fromAmount = 1000; component.exchange.toAmount = 1;
    const id = component.exchange.requestId; void component.saveExchange();
    http.expectOne('/api/finance/exchanges').flush({}, { status: 500, statusText: 'Error' }); tick();
    expect(component.exchange.requestId).toBe(id); expect(component.exchange.fromAmount).toBe(1000);
    void component.saveExchange(); const retry = http.expectOne('/api/finance/exchanges'); expect(retry.request.body.requestId).toBe(id);
    retry.flush({ data: id }); tick(); http.expectOne('/api/finance/overview').flush({ data: overview() });
    expect(component.exchange.requestId).not.toBe(id); component.ngOnDestroy();
  }));
  it('emite filtros para ver los cobros en su moneda y fecha efectiva', () => {
    const component = TestBed.runInInjectionContext(() => new FinanceOverviewComponent()); component.data = overview();
    spyOn(component.detail, 'emit'); component.showDetail({ currency: 'USD', movementType: 'Ingreso' });
    expect(component.detail.emit).toHaveBeenCalledWith({ currency: 'USD', movementType: 'Ingreso', from: '', to: '2025-02-28' }); component.ngOnDestroy();
  });
});
