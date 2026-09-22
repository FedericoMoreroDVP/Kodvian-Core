import { Component, EventEmitter, Input, Output } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { BehaviorSubject, Subject } from 'rxjs';
import { FinanceChartComponent } from './finance-chart.component';
import { VisionFinancieraPageComponent } from './vision-financiera-page.component';
import { FinanceOverview } from '../finanzas/models/finance-overview.models';

@Component({ selector: 'app-finance-chart', standalone: true, template: '' })
class ChartStub { @Input() model: any; @Output() selected = new EventEmitter<any>(); }

describe('Visión financiera: navegación y detalle', () => {
  let http: HttpTestingController;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let dialog: { open: jasmine.Spy };
  function data(): FinanceOverview {
    const currency = (code: string, income: number, expense: number) => ({ currency: code, income, expense, result: income - expense,
      recordedCashBalance: income - expense, cashChange: income - expense, balance: null, contributions: 0, withdrawals: 0, reimbursements: 0, pendingIncome: 20, pendingExpense: 10 });
    return { setup: { startDate: null, openingArs: null, openingUsd: null, historyComplete: false, version: 'v1' },
      from: null, to: '2025-09-22', currencies: [currency('ARS', 5200000, 400000), currency('USD', 200, 50)],
      months: [{ ...currency('ARS', 2500000, 0), year: 2025, month: 5 }, { ...currency('ARS', 2700000, 400000), recordedCashBalance: 4800000, year: 2025, month: 9 },
        { ...currency('USD', 200, 50), year: 2025, month: 5 }],
      expenses: [{ categoryId: 'publicidad', category: 'Publicidad', currency: 'ARS', amount: 400000 }, { categoryId: 'cloud', category: 'Servicios', currency: 'USD', amount: 50 }],
      partners: [], estimatedDateCount: 6, unclassifiedPayments: 0 };
  }
  beforeEach(() => {
    query = new BehaviorSubject(convertToParamMap({})); dialog = { open: jasmine.createSpy().and.returnValue({ afterClosed: () => new Subject() }) };
    TestBed.configureTestingModule({ imports: [VisionFinancieraPageComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
      { provide: MatDialog, useValue: dialog },
      { provide: ActivatedRoute, useFactory: () => ({ queryParamMap: query.asObservable(), snapshot: TestBed.inject(Router).routerState.snapshot.root }) }
    ] }).overrideComponent(VisionFinancieraPageComponent, { remove: { imports: [FinanceChartComponent] }, add: { imports: [ChartStub] } });
    spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function initialize() {
    const fixture = TestBed.createComponent(VisionFinancieraPageComponent); fixture.detectChanges();
    http.expectOne('/api/finance/overview').flush({ data: data() }); fixture.detectChanges(); return fixture;
  }
  it('muestra cuatro indicadores y gráficos sin montar formularios de configuración', () => {
    const fixture = initialize();
    expect(fixture.nativeElement.querySelectorAll('.metric-card').length).toBe(4);
    expect(fixture.nativeElement.querySelectorAll('app-finance-chart').length).toBe(3);
    expect(fixture.nativeElement.textContent).toContain('Visión financiera');
    expect(fixture.nativeElement.querySelector('input[name=start]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Punto de partida por configurar');
    http.expectNone('/api/finance/partners'); fixture.destroy();
  });
  it('mantiene filtros en la URL y cambia moneda sin repetir la consulta del histórico', () => {
    const fixture = initialize(); const component = fixture.componentInstance;
    component.selectCurrency('USD');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: jasmine.objectContaining({ moneda: 'USD', periodo: 'historico', seccion: 'resumen' }) }));
    query.next(convertToParamMap({ moneda: 'USD', periodo: 'historico', seccion: 'resumen' })); fixture.detectChanges();
    expect(component.row?.income).toBe(200); expect(component.expenses[0].categoryId).toBe('cloud');
    http.expectNone('/api/finance/overview'); fixture.destroy();
  });
  it('los gráficos abren sus movimientos y el neto abre un acumulado sin fecha inicial', () => {
    const fixture = initialize(); const component = fixture.componentInstance;
    component.flowSelected({ index: 0, datasetIndex: 0 });
    let detail = dialog.open.calls.mostRecent().args[1].data;
    expect(detail.filters).toEqual(jasmine.objectContaining({ view: 'OperationalIncome', dateFrom: '2025-05-01', dateTo: '2025-05-31' }));
    component.balanceSelected({ index: 1, datasetIndex: 0 }); detail = dialog.open.calls.mostRecent().args[1].data;
    expect(detail.filters.dateFrom).toBeUndefined(); expect(detail.filters.dateTo).toBe('2025-09-22'); expect(detail.amount).toBe(4800000);
    component.expenseSelected({ index: 0, datasetIndex: 0 }); detail = dialog.open.calls.mostRecent().args[1].data;
    expect(detail.filters.categoryId).toBe('publicidad'); expect(detail.filters.view).toBe('OperationalExpense'); fixture.destroy();
  });
  it('restaura un período personalizado y limita el detalle al rango visible', () => {
    query.next(convertToParamMap({ periodo: 'personalizado', moneda: 'ARS', seccion: 'resumen', desde: '2025-05-15', hasta: '2025-09-22' }));
    const fixture = TestBed.createComponent(VisionFinancieraPageComponent); fixture.detectChanges();
    const request = http.expectOne(r => r.url === '/api/finance/overview'); expect(request.request.params.get('from')).toBe('2025-05-15');
    request.flush({ data: { ...data(), from: '2025-05-15' } }); fixture.detectChanges();
    fixture.componentInstance.openMonth(fixture.componentInstance.buckets[0], 'income');
    expect(dialog.open.calls.mostRecent().args[1].data.filters.dateFrom).toBe('2025-05-15'); fixture.destroy();
  });
  it('evita consultar un rango inválido y permite recuperar errores de red', () => {
    query.next(convertToParamMap({ periodo: 'personalizado', desde: '2025-02-30', hasta: '2025-03-01' }));
    const fixture = TestBed.createComponent(VisionFinancieraPageComponent); fixture.detectChanges();
    http.expectNone(r => r.url === '/api/finance/overview'); expect(fixture.componentInstance.error).toContain('fechas válidas');
    query.next(convertToParamMap({})); http.expectOne('/api/finance/overview').flush({}, { status: 500, statusText: 'Error' }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No pudimos cargar'); fixture.componentInstance.reload();
    http.expectOne('/api/finance/overview').flush({ data: data() }); fixture.detectChanges(); expect(fixture.nativeElement.querySelectorAll('.metric-card').length).toBe(4); fixture.destroy();
  });
  it('carga socios y acuerdos solo al seleccionar sus secciones', () => {
    const fixture = initialize();
    query.next(convertToParamMap({ seccion: 'socios' })); http.expectOne('/api/finance/partners').flush({ data: [{ id: 'partner', fullName: 'Socio', isActive: true }] }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.partner-card').length).toBe(1);
    query.next(convertToParamMap({ seccion: 'compromisos' }));
    http.expectOne(r => r.url === '/api/finance/team-obligations').flush({ data: { items: [], totalCount: 0 } }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.commitment-card').length).toBe(2); fixture.destroy();
  });
  it('no mantiene importes de otro año si falla la consulta de acuerdos', () => {
    const fixture = initialize(); query.next(convertToParamMap({ seccion: 'compromisos' }));
    http.expectOne(r => r.url === '/api/finance/team-obligations').flush({ data: { totalCount: 1, items: [{
      contractId: 'contract', developerName: 'Miembro de prueba', projectName: 'Proyecto', paymentMode: 'FixedAmount', currency: 'ARS',
      currencies: [{ currency: 'ARS', totalDue: 100, totalPaid: 0, totalBalance: 100 }]
    }] } }); fixture.detectChanges(); expect(fixture.componentInstance.obligations.length).toBe(1);
    fixture.componentInstance.obligationsYear = 2024; fixture.componentInstance.loadObligations();
    http.expectOne(r => r.url === '/api/finance/team-obligations').flush({}, { status: 500, statusText: 'Error' }); fixture.detectChanges();
    expect(fixture.componentInstance.obligations).toEqual([]);
    expect(fixture.nativeElement.textContent).not.toContain('Miembro de prueba');
    expect(fixture.nativeElement.querySelector('.pagination')).toBeNull(); fixture.destroy();
  });
});
