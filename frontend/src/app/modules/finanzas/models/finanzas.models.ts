export type { ApiResponse, PagedResult } from '../../../shared/models/api.models';

export type TipoMovimiento = 'Ingreso' | 'Egreso';
export type EstadoMovimiento = 'Pendiente' | 'Cobrado' | 'Pagado' | 'Vencido' | 'Anulado';

export interface CategoriaFinanciera {
  id: string;
  name: string;
  movementType: TipoMovimiento;
  isActive: boolean;
}

export interface Proveedor {
  id: string;
  name: string;
  taxId?: string;
  email?: string;
  phone?: string;
  isActive: boolean;
}

export interface MovimientoListado {
  currency: string;
  nature: string;
  settlementDate?: string;
  exchangeId?: string;
  developerPaymentId?: string;
  id: string;
  movementType: TipoMovimiento;
  categoryName: string;
  description: string;
  amount: number;
  movementDate: string;
  dueDate?: string;
  status: EstadoMovimiento;
  paymentMethod?: string;
  clientName?: string;
  providerName?: string;
}

export interface MovimientoDetalle {
  currency: string;
  nature: string;
  funding: string;
  partnerId?: string;
  settlementDate?: string;
  settlementDateEstimated: boolean;
  version: string;
  exchangeId?: string;
  developerPaymentId?: string;
  id: string;
  movementType: TipoMovimiento;
  categoryId: string;
  categoryName: string;
  clientId?: string;
  clientName?: string;
  providerId?: string;
  providerName?: string;
  projectId?: string;
  projectName?: string;
  description: string;
  amount: number;
  movementDate: string;
  dueDate?: string;
  status: EstadoMovimiento;
  paymentMethod?: string;
  receiptNumber?: string;
  notes?: string;
  createdById: string;
  createdByName: string;
  createdAt: string;
  updatedAt?: string;
}

export interface MovimientoFormulario {
  requestId: string;
  currency: string;
  nature: string;
  funding: string;
  partnerId?: string | null;
  settlementDate?: string | null;
  settlementDateEstimated: boolean;
  expectedVersion?: string;
  movementType: TipoMovimiento;
  categoryId: string;
  clientId?: string | null;
  providerId?: string | null;
  projectId?: string | null;
  description: string;
  amount: number;
  movementDate: string;
  dueDate?: string | null;
  status: EstadoMovimiento;
  paymentMethod?: string;
  receiptNumber?: string;
  notes?: string;
  receiptFile?: File | null;
}

export interface ComprobanteArchivo {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByName: string;
  uploadedAt: string;
}

export interface FinanzaFiltros {
  currency?: string;
  nature?: string;
  projectId?: string;
  unlinkedOnly?: boolean;
  useSettlementDate?: boolean;
  exactAmount?: number;
  pageNumber: number;
  pageSize: number;
  dateFrom?: string;
  dateTo?: string;
  movementType?: TipoMovimiento | '';
  categoryId?: string;
  clientId?: string;
  providerId?: string;
  status?: EstadoMovimiento | '';
}

export interface LookupItem {
  id: string;
  name: string;
}

export interface FinanzasLookups {
  categories: CategoriaFinanciera[];
  clients: LookupItem[];
  projects: LookupItem[];
  providers: LookupItem[];
}

export interface ResumenMensual {
  currencies: import('./finance-overview.models').CurrencyPeriod[];
  monthlyIncome: number;
  monthlyExpense: number;
  monthlyResult: number;
  pendingIncome: number;
  pendingExpense: number;
}

export interface CategoriaFormulario {
  name: string;
  movementType: TipoMovimiento;
  isActive: boolean;
}
