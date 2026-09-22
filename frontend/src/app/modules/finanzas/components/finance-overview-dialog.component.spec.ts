import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { FinanceOverviewDialogComponent } from './finance-overview-dialog.component';

describe('Modal histórico financiero', () => {
  it('consulta el acumulado al abrirse y evita cerrarse durante un guardado', () => {
    const ref = { close: jasmine.createSpy(), disableClose: false };
    TestBed.configureTestingModule({ imports: [FinanceOverviewDialogComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), { provide: MatDialogRef, useValue: ref }] });
    const fixture = TestBed.createComponent(FinanceOverviewDialogComponent); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/finance/overview').flush({ data: {
      setup: { startDate: null, openingArs: null, openingUsd: null, historyComplete: false, version: 'v' }, from: null, to: '2025-09-22',
      currencies: [{ currency: 'ARS', income: 5200000, expense: 400000, result: 4800000, recordedCashBalance: 4800000, balance: null,
        cashChange: 4800000, contributions: 0, withdrawals: 0, reimbursements: 0, pendingIncome: 0, pendingExpense: 0 }],
      months: [], partners: [], expenses: [], estimatedDateCount: 6, unclassifiedPayments: 0
    } }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('4,800,000.00');
    expect(fixture.nativeElement.textContent).toContain('Saldo neto registrado');
    expect(fixture.nativeElement.textContent).not.toContain('Punto de partida por configurar');
    fixture.componentInstance.setBusy(true); fixture.componentInstance.close(); expect(ref.close).not.toHaveBeenCalled(); expect(ref.disableClose).toBeTrue();
    fixture.componentInstance.setBusy(false);
    fixture.componentInstance.viewDetail({ currency: 'ARS', movementType: 'Ingreso', from: '', to: '2025-09-22' });
    expect(ref.close).toHaveBeenCalledWith({ detail: { currency: 'ARS', movementType: 'Ingreso', from: '', to: '2025-09-22' } });
    http.verify(); fixture.destroy();
  });
});
