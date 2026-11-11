export interface FinanceSetup { startDate: string | null; openingArs: number | null; openingUsd: number | null; historyComplete: boolean; version: string; }
export interface CurrencyPeriod { currency: string; income: number; expense: number; result: number; pendingIncome: number; pendingExpense: number; }
export interface FinancePeriodSummary { from: string; to: string; currencies: CurrencyPeriod[]; }
export interface CurrencyOverview extends CurrencyPeriod { contributions: number; withdrawals: number; reimbursements: number; cashChange: number; recordedCashBalance: number; balance: number | null; }
export interface FinanceMonth extends CurrencyOverview { year: number; month: number; }
export type PartnerSource = 'Manual' | 'Developer' | 'User';
export interface Partner { id: string; fullName: string; isActive: boolean; participationPercentage?: number; email?: string | null; source?: PartnerSource; personId?: string | null; }
export interface PartnerPerson { source: 'Developer' | 'User'; personId: string; fullName: string; email?: string | null; registeredPartnerId?: string | null; }
export interface PartnerDetails { source?: PartnerSource; personId?: string | null; email?: string | null; }
export interface PartnerBalance { partnerId: string; partnerName: string; currency: string; contributions: number; withdrawals: number; reimbursableExpenses: number; reimbursements: number; outstanding: number; }
export interface IncomeExpenseCoverage { expenseMovementId: string; description: string; amount: number; }
export interface IncomePartnerDistribution { partnerId: string; partnerName: string; percentage: number; amount: number; }
export interface IncomeAllocation { incomeMovementId: string; settlementDate: string; description: string; currency: string; grossAmount: number; expensesCovered: number; capitalizedAmount: number; distributableAmount: number; sharesConfigured: boolean; expenses: IncomeExpenseCoverage[]; partners: IncomePartnerDistribution[]; }
export interface CapitalAllocationMonth { year: number; month: number; grossIncome: number; expensesCovered: number; capitalized: number; distributable: number; cumulativeCapital: number; }
export interface PendingExpenseForAllocation { expenseMovementId: string; description: string; movementDate: string; remainingAmount: number; }
export interface CapitalAllocationCurrency { currency: string; openingCapital: number; capitalizedInYear: number; distributableInYear: number; expensesAwaitingCoverage: number; pendingExpenses: PendingExpenseForAllocation[]; months: CapitalAllocationMonth[]; receipts: IncomeAllocation[]; }
export interface CapitalAllocationReport { year: number; currencies: CapitalAllocationCurrency[]; }
export interface FinanceOverview { setup: FinanceSetup; from: string | null; to: string; estimatedDateCount: number; unclassifiedPayments: number; currencies: CurrencyOverview[]; months: FinanceMonth[]; expenses: { currency: string; category: string; categoryId: string; amount: number }[]; partners: PartnerBalance[]; }
export const FINANCE_NATURES = [
  { value: 'Operacion', label: 'Operación de la empresa' }, { value: 'AporteSocio', label: 'Aporte de socio' },
  { value: 'RetiroSocio', label: 'Retiro de socio' }, { value: 'ReintegroSocio', label: 'Reintegro a socio' }
];
