import { FinanceMonth } from '../finanzas/models/finance-overview.models';

export type VisionSection = 'resumen' | 'socios' | 'compromisos';
export type VisionPeriod = 'historico' | 'mes' | 'anio' | 'personalizado';
export interface VisionState { section: VisionSection; period: VisionPeriod; currency: 'ARS' | 'USD'; from: string; to: string; }
export interface VisionBucket { key: string; label: string; from: string; to: string; income: number; expense: number; result: number; balance: number; }
export interface ChartSeries { label: string; values: number[]; color: string; }
export interface FinanceChartModel { type: 'bar' | 'line'; horizontal?: boolean; labels: string[]; fullLabels?: string[]; series: ChartSeries[]; currency: string; description: string; }
export interface ChartSelection { index: number; datasetIndex: number; }

export function money(value: number, currency: string, compact = false): string {
  return new Intl.NumberFormat('es-AR', { style: 'currency', currency, currencyDisplay: 'code',
    notation: compact && Math.abs(value) >= 10000 ? 'compact' : 'standard',
    minimumFractionDigits: compact ? 0 : 2, maximumFractionDigits: compact ? 1 : 2 }).format(value);
}
export function dateLabel(value?: string | null): string {
  if (!value) return 'el primer movimiento';
  const parsed = new Date(value + 'T00:00:00Z');
  if (Number.isNaN(parsed.valueOf())) return 'Fecha inválida';
  return new Intl.DateTimeFormat('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC' }).format(parsed);
}
export function isoDate(value: Date): string {
  return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`;
}
export function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(value + 'T00:00:00Z');
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === value && +value.slice(0, 4) >= 2000 && +value.slice(0, 4) <= 2100;
}
export function periodRange(state: VisionState, today = new Date()): { from: string; to: string } {
  if (state.period === 'mes') return { from: isoDate(new Date(today.getFullYear(), today.getMonth(), 1)), to: isoDate(new Date(today.getFullYear(), today.getMonth() + 1, 0)) };
  if (state.period === 'anio') return { from: `${today.getFullYear()}-01-01`, to: `${today.getFullYear()}-12-31` };
  if (state.period === 'personalizado') return { from: state.from, to: state.to };
  return { from: '', to: '' };
}
export function parseVisionState(query: { get(name: string): string | null }): VisionState {
  const period = query.get('periodo'); const section = query.get('seccion');
  return {
    period: (['historico', 'mes', 'anio', 'personalizado'].includes(period ?? '') ? period : 'historico') as VisionPeriod,
    section: (['resumen', 'socios', 'compromisos'].includes(section ?? '') ? section : 'resumen') as VisionSection,
    currency: query.get('moneda') === 'USD' ? 'USD' : 'ARS', from: query.get('desde') ?? '', to: query.get('hasta') ?? ''
  };
}

export function monthlyBuckets(months: FinanceMonth[], currency: string): { grouping: string; buckets: VisionBucket[] } {
  const rows = months.filter(x => x.currency === currency).sort((a, b) => a.year - b.year || a.month - b.month);
  const step = rows.length <= 24 ? 1 : rows.length <= 72 ? 3 : 12;
  const map = new Map<string, VisionBucket>();
  for (const row of rows) {
    const groupMonth = Math.floor((row.month - 1) / step) * step + 1;
    const key = `${row.year}-${groupMonth}`;
    const label = step === 12 ? String(row.year) : step === 3 ? `${Math.floor((row.month - 1) / 3) + 1}.º trim. ${row.year}`
      : new Intl.DateTimeFormat('es-AR', { month: 'short', year: '2-digit' }).format(new Date(row.year, row.month - 1, 1));
    const bucket = map.get(key) ?? { key, label, from: isoDate(new Date(row.year, row.month - 1, 1)), to: '', income: 0, expense: 0, result: 0, balance: 0 };
    // Sum flows in cents. A balance is a stock: keep its last value, never add balances.
    bucket.income = (Math.round(bucket.income * 100) + Math.round(row.income * 100)) / 100;
    bucket.expense = (Math.round(bucket.expense * 100) + Math.round(row.expense * 100)) / 100;
    bucket.result = (Math.round(bucket.income * 100) - Math.round(bucket.expense * 100)) / 100;
    bucket.balance = row.recordedCashBalance;
    bucket.to = isoDate(new Date(row.year, row.month, 0)); map.set(key, bucket);
  }
  return { grouping: step === 1 ? 'Mensual' : step === 3 ? 'Trimestral' : 'Anual', buckets: [...map.values()] };
}
