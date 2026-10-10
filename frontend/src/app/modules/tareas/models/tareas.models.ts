export type { ApiResponse, PagedResult } from '../../../shared/models/api.models';

export type EstadoTarea = 'Pendiente' | 'EnCurso' | 'Bloqueada' | 'Finalizada' | 'Cancelada';
export type PrioridadTarea = 'Baja' | 'Media' | 'Alta' | 'Urgente';

export interface TareaListado {
  id: string;
  projectId: string;
  projectName: string;
  title: string;
  developerId?: string;
  developerName?: string;
  responsibleId?: string;
  responsibleName?: string;
  status: EstadoTarea;
  priority: PrioridadTarea;
  dueDate?: string;
  finishedDate?: string;
  estimatedHours?: number;
  realHours?: number;
  kanbanOrder: number;
  isActive: boolean;
}

export interface TareaDetalle {
  id: string;
  projectId: string;
  projectName: string;
  title: string;
  description?: string;
  developerId?: string;
  developerName?: string;
  responsibleId?: string;
  responsibleName?: string;
  createdById: string;
  createdByName: string;
  status: EstadoTarea;
  priority: PrioridadTarea;
  startDate?: string;
  dueDate?: string;
  finishedDate?: string;
  estimatedHours?: number;
  realHours?: number;
  kanbanOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface TareaFormulario {
  projectId: string;
  title: string;
  description?: string;
  developerId?: string | null;
  responsibleId?: string | null;
  status: EstadoTarea;
  priority: PrioridadTarea;
  startDate?: string | null;
  dueDate?: string | null;
  finishedDate?: string | null;
  estimatedHours?: number | null;
  realHours?: number | null;
  kanbanOrder: number;
  isActive: boolean;
}

export interface TareaFiltros {
  pageNumber: number;
  pageSize: number;
  search?: string;
  projectId?: string;
  developerId?: string;
  responsibleId?: string;
  status?: EstadoTarea | '';
  priority?: PrioridadTarea | '';
  dueDateFrom?: string;
  dueDateTo?: string;
  completedHistory?: boolean;
}

export interface LookupItem {
  id: string;
  name: string;
}

export interface TareaLookups {
  projects: LookupItem[];
  developers: LookupItem[];
}

export interface KanbanItem {
  id: string;
  title: string;
  projectName: string;
  developerName?: string;
  responsibleName?: string;
  priority: PrioridadTarea;
  dueDate?: string;
  kanbanOrder: number;
}

export interface KanbanColumn {
  status: EstadoTarea;
  title: string;
  items: KanbanItem[];
}
