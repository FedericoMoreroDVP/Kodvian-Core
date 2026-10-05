import { PagedResult } from '../../../shared/models/api.models';

export type PrioridadReunion = 'Baja' | 'Media' | 'Alta' | 'Urgente';
export type EstadoReunion = 'Programada' | 'Confirmada' | 'Realizada' | 'Cancelada';

export interface AgendaLookupItem { id: string; name: string; calendarColor?: string; }
export interface ReunionParticipante extends AgendaLookupItem {}
export interface Reunion {
  id: string;
  projectId: string;
  projectName: string;
  projectCalendarColor: string;
  title: string;
  description?: string;
  startsAt: string;
  endsAt: string;
  priority: PrioridadReunion;
  status: EstadoReunion;
  link?: string;
  location?: string;
  createdById: string;
  createdByName: string;
  participants: ReunionParticipante[];
}

export interface ReunionFormulario {
  projectId: string;
  title: string;
  description?: string;
  startsAt: string;
  endsAt: string;
  priority: PrioridadReunion;
  status: EstadoReunion;
  link?: string;
  location?: string;
  participantIds: string[];
}

export interface AgendaFiltros {
  pageNumber: number;
  pageSize: number;
  projectId?: string;
  priority?: PrioridadReunion | '';
  status?: EstadoReunion | '';
  from: string;
  to: string;
}

export interface AgendaLookups { projects: AgendaLookupItem[]; users: AgendaLookupItem[]; }
export type AgendaResultado = PagedResult<Reunion>;
