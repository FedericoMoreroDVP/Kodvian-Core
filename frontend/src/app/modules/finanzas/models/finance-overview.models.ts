export interface FinanceSetup { startDate: string | null; openingArs: number | null; openingUsd: number | null; historyComplete: boolean; version: string; }
export interface CurrencyPeriod { currency: string; income: number; expense: number; result: number; pendingIncome: number; pendingExpense: number; }
export interface FinancePeriodSummary { from: string; to: string; currencies: CurrencyPeriod[]; }
export interface CurrencyOverview extends CurrencyPeriod { contributions: number; withdrawals: number; reimbursements: number; cashChange: number; recordedCashBalance: number; balance: number | null; }
export interface FinanceMonth extends CurrencyOverview { year: number; month: number; }
export interface Partner { id: string; fullName: string; isActive: boolean; }
export interface PartnerBalance { partnerId: string; partnerName: string; currency: string; contributions: number; withdrawals: number; reimbursableExpenses: number; reimbursements: number; outstanding: number; }
export interface FinanceOverview { setup: FinanceSetup; from: string | null; to: string; estimatedDateCount: number; unclassifiedPayments: number; currencies: CurrencyOverview[]; months: FinanceMonth[]; expenses: { currency: string; category: string; categoryId: string; amount: number }[]; partners: PartnerBalance[]; }
export const FINANCE_NATURES = [
  { value: 'Operacion', label: 'Operación de la empresa' }, { value: 'AporteSocio', label: 'Aporte de socio' },
  { value: 'RetiroSocio', label: 'Retiro de socio' }, { value: 'ReintegroSocio', label: 'Reintegro a socio' }
];
