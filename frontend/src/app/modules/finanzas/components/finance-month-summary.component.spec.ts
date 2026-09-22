import { TestBed } from '@angular/core/testing';
import { FinanceMonthSummaryComponent } from './finance-month-summary.component';

describe('Resumen mensual compacto', () => {
  it('muestra solo tres indicadores y cambia de moneda sin exponer el histórico', () => {
    const fixture = TestBed.createComponent(FinanceMonthSummaryComponent);
    fixture.componentInstance.rows = [
      { currency: 'ARS', income: 0, expense: 400000, result: -400000, pendingIncome: 0, pendingExpense: 0 },
      { currency: 'USD', income: 100, expense: 20, result: 80, pendingIncome: 0, pendingExpense: 0 }
    ];
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('article').length).toBe(3);
    expect(fixture.nativeElement.textContent).toContain('Gastos pagados del mes');
    expect(fixture.nativeElement.textContent).toContain('400,000.00');
    expect(fixture.nativeElement.textContent).not.toContain('Punto de partida');
    expect(fixture.nativeElement.textContent).not.toContain('Aportes');
    const buttons = fixture.nativeElement.querySelectorAll('button');
    buttons[1].click(); fixture.detectChanges();
    expect(fixture.componentInstance.currency).toBe('USD');
    expect(fixture.nativeElement.textContent).toContain('80.00');
    expect(fixture.nativeElement.textContent).not.toContain('400,000.00');
    fixture.destroy();
  });
});
