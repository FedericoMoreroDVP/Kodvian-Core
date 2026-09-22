export type { ApiResponse } from '../../../shared/models/api.models';

export interface DashboardKpis {
  activeClients: number;
  projectsInProgress: number;
  overdueTasks: number;
  tasksForToday: number;
  monthlyIncome: number;
  monthlyExpense: number;
  monthlyResult: number;
  pendingCollections: number;
  pendingPayments: number;
}

export interface PriorityTask {
  id: string;
  title: string;
  projectName: string;
  responsibleName?: string;
  status: string;
  priority: string;
  dueDate?: string;
}

export interface UpcomingCollection {
  currency: string;
  id: string;
  description: string;
  categoryName: string;
  clientName?: string;
  amount: number;
  dueDate?: string;
  status: string;
}

export interface RecentMovement {
  currency: string;
  id: string;
  movementType: string;
  description: string;
  categoryName: string;
  amount: number;
  movementDate: string;
  status: string;
}

export interface DashboardOverview {
  finance?: import('../../finanzas/models/finance-overview.models').FinancePeriodSummary;
  kpis: DashboardKpis;
  priorityTasks: PriorityTask[];
  upcomingCollections: UpcomingCollection[];
  recentMovements: RecentMovement[];
}
