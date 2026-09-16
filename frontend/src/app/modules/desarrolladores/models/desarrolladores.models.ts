export type { ApiResponse } from '../../../shared/models/api.models';

export interface DesarrolladorExterno {
  id: string;
  fullName: string;
  email?: string;
  phone?: string;
  taxId?: string;
  notes?: string;
  isActive: boolean;
  hasSystemAccess: boolean;
  isSystemAccessActive: boolean;
  roles: string[];
}

export interface DesarrolladorFormulario {
  fullName: string;
  email?: string;
  phone?: string;
  taxId?: string;
  notes?: string;
  isActive: boolean;
  enableSystemAccess: boolean;
  isSystemAccessActive: boolean;
  accessPassword?: string;
}

export interface TeamUser {
  id: string;
  fullName: string;
  email: string;
  role: string;
  roles: string[];
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface TeamUserFormulario {
  fullName: string;
  email: string;
  password?: string;
  isActive: boolean;
}

export interface ResumenContratoDesarrollador {
  contractId: string;
  projectId: string;
  projectName: string;
  paymentMode: 'Percentage' | 'FixedAmount' | string;
  percentage?: number;
  agreedAmount?: number;
  totalDue: number;
  totalPaid: number;
  totalBalance: number;
  isUpToDate: boolean;
  lastPaymentDate?: string;
  isActive: boolean;
}
