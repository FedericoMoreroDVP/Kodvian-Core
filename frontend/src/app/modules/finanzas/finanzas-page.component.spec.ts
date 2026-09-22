import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNativeDateAdapter } from '@angular/material/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';
import { FinanzasPageComponent } from './finanzas-page.component';

describe('Finanzas: histórico bajo demanda', () => {
  let http: HttpTestingController;
  let dialog: { open: jasmine.Spy };
  let closed: Subject<any>;
  const monthly = { monthlyIncome: 0, monthlyExpense: 400000, monthlyResult: -400000, pendingIncome: 0, pendingExpense: 0,
    currencies: [{ currency: 'ARS', income: 0, expense: 400000, result: -400000, pendingIncome: 0, pendingExpense: 0 }] };
  beforeEach(() => {
    closed = new Subject(); dialog = { open: jasmine.createSpy().and.returnValue({ afterClosed: () => closed }) };
    TestBed.configureTestingModule({ imports: [FinanzasPageComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideNativeDateAdapter(),
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      { provide: Router, useValue: { navigate: jasmine.createSpy() } },
      { provide: MatDialog, useValue: dialog }, { provide: MatSnackBar, useValue: { open: jasmine.createSpy() } }
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function initialize() {
    const fixture = TestBed.createComponent(FinanzasPageComponent); fixture.detectChanges();
    http.expectOne('/api/financial-movements/lookups').flush({ data: { categories: [], clients: [], projects: [], providers: [] } });
    http.expectOne('/api/financial-categories').flush({ data: [] });
    http.expectOne('/api/financial-movements/monthly-summary').flush({ data: monthly });
    http.expectOne(r => r.url === '/api/financial-movements').flush({ data: { items: [], totalCount: 0 } });
    fixture.detectChanges(); return fixture;
  }
  it('no monta ni consulta el histórico al entrar y mantiene visibles los movimientos', () => {
    const fixture = initialize();
    expect(fixture.nativeElement.querySelector('app-finance-overview')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-finance-month-summary')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.tabla-movimientos')).not.toBeNull();
    expect(dialog.open).not.toHaveBeenCalled();
    http.expectNone(r => r.url.startsWith('/api/finance/'));
    expect(fixture.componentInstance.filtrosAvanzados).toBeFalse(); fixture.destroy();
  });
  it('abre el modal por acción explícita y aplica sus filtros de cobros al cerrarse', async () => {
    const fixture = initialize();
    await fixture.componentInstance.abrirHistorico();
    expect(dialog.open).toHaveBeenCalledTimes(1);
    await fixture.componentInstance.abrirHistorico();
    expect(dialog.open).toHaveBeenCalledTimes(1);
    closed.next({ detail: { currency: 'ARS', movementType: 'Ingreso', from: '', to: '2025-09-22' } }); closed.complete();
    http.expectOne('/api/financial-movements/monthly-summary').flush({ data: monthly });
    const request = http.expectOne(r => r.url === '/api/financial-movements');
    expect(request.request.params.get('status')).toBe('Cobrado');
    expect(request.request.params.get('useSettlementDate')).toBe('true');
    expect(request.request.params.has('dateFrom')).toBeFalse();
    request.flush({ data: { items: [], totalCount: 0 } });
    expect(fixture.componentInstance.historicoAbierto).toBeFalse();
    expect(fixture.componentInstance.filtrosAvanzados).toBeTrue(); fixture.destroy();
  });
});
