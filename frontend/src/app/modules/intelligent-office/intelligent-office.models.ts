export interface AiConversation {
  id: string;
  title: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  messageCount: number;
  isPinned?: boolean;
}

export interface AiSourceReference {
  toolName: string;
  source: string;
  asOfUtc: string;
  from: string | null;
  to: string | null;
  currency: string | null;
  link: string | null;
}

export interface AiMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAtUtc: string;
  traceId: string | null;
  sources: AiSourceReference[];
  /** false cuando la respuesta no consultó datos de Kodvian. */
  grounded?: boolean;
  pendingActions?: AiPendingAction[];
  /** Próximos pasos sugeridos por el asistente (se muestran como botones). */
  suggestions?: string[];
  references?: AiEntityReference[];
  visual?: AiVisual | null;
  feedback?: number | null;
}

export type AiStreamEvent =
  | { type: 'status'; data: { status: 'thinking' | 'tool'; label: string; toolName: string | null } }
  | { type: 'delta'; data: { text: string } }
  | { type: 'reset'; data: Record<string, never> }
  | { type: 'done'; data: AiSendResult }
  | { type: 'error'; data: { status: number; message: string } };

export type AiPendingActionStatus = 'Pending' | 'Executing' | 'Confirmed' | 'Cancelled' | 'Expired' | 'Failed' | 'Undone';

export interface AiActionDetail {
  label: string;
  value: string;
}

export interface AiPendingAction {
  id: string;
  toolName: string;
  title: string;
  summary: string;
  details: AiActionDetail[];
  status: AiPendingActionStatus;
  expiresAtUtc: string;
  resolvedAtUtc: string | null;
  resultMessage: string | null;
  resultLink: string | null;
  canUndo?: boolean;
  undoAvailableUntilUtc?: string | null;
}

export interface AiActionResolution {
  action: AiPendingAction;
  message: AiMessage | null;
}

export interface AiConversationDetail {
  id: string;
  title: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  messageCount: number;
  messages: AiMessage[];
}

export interface AiSendResult {
  userMessage: AiMessage;
  assistantMessage: AiMessage;
  runId: string;
  traceId: string;
}

export interface Paged<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
}

export interface AiActionBatchResolution {
  actions: AiPendingAction[];
  message: AiMessage | null;
}

export interface AiBriefingItem {
  id: string;
  title: string;
  detail: string | null;
}

export interface AiAlert {
  kind: 'overdue_tasks' | 'project_risk' | 'expense_spike' | 'overdue_collections' | string;
  severity: 'info' | 'warning' | 'critical';
  title: string;
  detail: string;
  prompt: string;
  link: string | null;
}

export interface AiBriefing {
  date: string;
  greeting: string | null;
  tasks: { overdue: number; dueToday: number; dueThisWeek: number; topOverdue: AiBriefingItem[] } | null;
  meetings: { today: number; thisWeek: number; next: AiBriefingItem[] } | null;
  collections: { currency: string; pending: number; overdue: number; count: number }[] | null;
  projectsAtRisk: { id: string; name: string; progress: number; estimatedDelivery: string | null; daysToDelivery: number | null }[] | null;
  alerts: AiAlert[];
}

export interface AiUserPreferences {
  preferredCurrency: 'ARS' | 'USD' | null;
  defaultMeetingMinutes: number | null;
  defaultTaskPriority: string | null;
  responseStyle: 'breve' | 'detallado' | null;
  defaultScope: 'todo' | 'mio' | null;
  notes: string | null;
  updatedAtUtc?: string | null;
}

export interface AiEntityReference {
  kind: 'Tarea' | 'Reunión' | 'Proyecto' | 'Cliente' | 'Usuario' | 'Desarrollador' | string;
  name: string;
  id: string;
}

/** Gráfico ingresos vs gastos: un grupo por moneda (nunca se mezclan ARS y USD). */
export interface AiVisual {
  title: string;
  groups: { currency: string; labels: string[]; datasets: { label: string; values: number[] }[] }[];
}
