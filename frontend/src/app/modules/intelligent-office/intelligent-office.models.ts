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
