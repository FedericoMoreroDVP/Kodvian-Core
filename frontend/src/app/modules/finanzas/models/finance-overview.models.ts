export interface FinanceSetup { startDate: string | null; openingArs: number | null; openingUsd: number | null; historyComplete: boolean; version: string; }
export interface CurrencyOverview { currency: string; income: number; expense: number; result: number; contributions: number; withdrawals: number; reimbursements: number; pendingIncome: number; pendingExpense: number; cashChange: number; balance: number | null; }
export interface FinanceMonth extends CurrencyOverview { year: number; month: number; }
export interface Partner { id: string; fullName: string; isActive: boolean; }
export interface PartnerBalance { partnerId: string; partnerName: string; currency: string; contributions: number; withdrawals: number; reimbursableExpenses: number; reimbursements: number; outstanding: number; }
export interface FinanceOverview { setup: FinanceSetup; from: string | null; to: string; estimatedDateCount: number; unclassifiedPayments: number; currencies: CurrencyOverview[]; months: FinanceMonth[]; expenses: { currency: string; category: string; amount: number }[]; partners: PartnerBalance[]; }
export const FINANCE_NATURES = [
  { value: 'Operacion', label: 'Operación de la empresa' }, { value: 'AporteSocio', label: 'Aporte de socio' },
  { value: 'RetiroSocio', label: 'Retiro de socio' }, { value: 'ReintegroSocio', label: 'Reintegro a socio' }
];
