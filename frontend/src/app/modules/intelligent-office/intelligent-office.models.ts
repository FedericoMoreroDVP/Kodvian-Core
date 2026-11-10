export interface AiConversation {
  id: string;
  title: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  messageCount: number;
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
