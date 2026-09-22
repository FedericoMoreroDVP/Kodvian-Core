import { convertToParamMap } from '@angular/router';
import { FinanceMonth } from '../finanzas/models/finance-overview.models';
import { money, monthlyBuckets, parseVisionState, periodRange, validDate } from './vision.models';

describe('Datos de Visión financiera', () => {
  function months(count: number): FinanceMonth[] {
    return Array.from({ length: count }, (_, i) => ({ currency: 'ARS', year: 2024 + Math.floor(i / 12), month: i % 12 + 1,
      income: 10, expense: 3, result: 7, cashChange: 7, recordedCashBalance: (i + 1) * 7, balance: null,
      contributions: 0, withdrawals: 0, reimbursements: 0, pendingIncome: 0, pendingExpense: 0 }));
  }
  it('agrupa flujos por trimestre pero conserva el último saldo, sin sumarlo', () => {
    const result = monthlyBuckets(months(27), 'ARS');
    expect(result.grouping).toBe('Trimestral');
    expect(result.buckets[0]).toEqual(jasmine.objectContaining({ income: 30, expense: 9, result: 21, balance: 21, from: '2024-01-01', to: '2024-03-31' }));
    expect(monthlyBuckets(months(90), 'ARS').buckets[0].balance).toBe(84);
  });
  it('no combina monedas y mantiene períodos mensuales cortos', () => {
    const rows = [...months(2), { ...months(1)[0], currency: 'USD', income: 999 }];
    const result = monthlyBuckets(rows, 'ARS');
    expect(result.grouping).toBe('Mensual'); expect(result.buckets.length).toBe(2);
    expect(result.buckets.reduce((sum, x) => sum + x.income, 0)).toBe(20);
    expect(monthlyBuckets(rows, 'USD').buckets[0].income).toBe(999);
  });
  it('restaura período, moneda y sección desde la URL y valida fechas reales', () => {
    const state = parseVisionState(convertToParamMap({ periodo: 'personalizado', moneda: 'USD', seccion: 'socios', desde: '2025-05-15', hasta: '2025-09-22' }));
    expect(state.currency).toBe('USD'); expect(state.section).toBe('socios');
    expect(periodRange(state)).toEqual({ from: '2025-05-15', to: '2025-09-22' });
    expect(validDate('2025-02-30')).toBeFalse(); expect(validDate('2024-02-29')).toBeTrue();
    expect(parseVisionState(convertToParamMap({ moneda: 'EUR', periodo: 'invalid' })).currency).toBe('ARS');
  });
  it('calcula mes y año, y utiliza formato español para importes exactos', () => {
    const state = parseVisionState(convertToParamMap({ periodo: 'mes' }));
    expect(periodRange(state, new Date(2025, 8, 22))).toEqual({ from: '2025-09-01', to: '2025-09-30' });
    expect(periodRange({ ...state, period: 'anio' }, new Date(2025, 8, 22))).toEqual({ from: '2025-01-01', to: '2025-12-31' });
    expect(money(5200000, 'ARS')).toContain('5.200.000,00');
  });
});
