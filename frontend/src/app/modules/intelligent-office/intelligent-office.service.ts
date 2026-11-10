import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';

import { ApiResponse } from '../../core/auth/auth.models';
import { AiActionResolution, AiConversation, AiConversationDetail, AiSendResult, AiStreamEvent, Paged } from './intelligent-office.models';

/** El streaming no está disponible: hay que usar el envío común. */
export class StreamUnavailableError extends Error {
  constructor() { super('stream_unavailable'); }
}

function parseSseEvent(block: string): AiStreamEvent | null {
  let type = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) type = line.slice(6).trim();
    else if (line.startsWith('data:')) data.push(line.slice(5).trimStart());
  }
  if (!data.length) return null;
  try {
    return { type, data: JSON.parse(data.join('\n')) } as AiStreamEvent;
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class IntelligentOfficeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/ai/conversations';

  list(pageNumber = 1): Observable<Paged<AiConversation>> {
    return this.http.get<ApiResponse<Paged<AiConversation>>>(`${this.baseUrl}?pageNumber=${pageNumber}&pageSize=50`, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  create(): Observable<AiConversation> {
    return this.http.post<ApiResponse<AiConversation>>(this.baseUrl, {}, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  get(id: string): Observable<AiConversationDetail> {
    return this.http.get<ApiResponse<AiConversationDetail>>(`${this.baseUrl}/${encodeURIComponent(id)}`, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  send(id: string, content: string, clientRequestId: string): Observable<AiSendResult> {
    return this.http.post<ApiResponse<AiSendResult>>(`${this.baseUrl}/${encodeURIComponent(id)}/messages`,
      { content, clientRequestId }, { withCredentials: true }).pipe(map(response => response.data));
  }

  /**
   * Envía el mensaje y recibe la respuesta en vivo (server-sent events): progreso por herramienta,
   * texto a medida que llega y el resultado final. Usa fetch porque EventSource solo admite GET.
   * Si el streaming no está disponible (ruta inexistente, sesión vencida, error de red antes de empezar)
   * emite StreamUnavailableError para que la pantalla use el envío común como respaldo.
   */
  sendStream(id: string, content: string, clientRequestId: string): Observable<AiStreamEvent> {
    const url = `${this.baseUrl}/${encodeURIComponent(id)}/messages/stream`;
    return new Observable<AiStreamEvent>(subscriber => {
      const controller = new AbortController();
      void (async () => {
        let response: Response;
        try {
          response = await fetch(url, {
            method: 'POST',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
            body: JSON.stringify({ content, clientRequestId }),
            signal: controller.signal
          });
        } catch {
          if (!controller.signal.aborted) subscriber.error(new StreamUnavailableError());
          return;
        }

        const contentType = response.headers.get('content-type') ?? '';
        if (!response.ok || !contentType.includes('text/event-stream') || !response.body) {
          if (response.ok || [401, 404, 405, 415].includes(response.status)) { subscriber.error(new StreamUnavailableError()); return; }
          let body: unknown = null;
          try { body = await response.json(); } catch { body = null; }
          subscriber.error(new HttpErrorResponse({ status: response.status, error: body, url }));
          return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        try {
          for (;;) {
            const { done, value } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
            let separator = buffer.indexOf('\n\n');
            while (separator >= 0) {
              const event = parseSseEvent(buffer.slice(0, separator));
              buffer = buffer.slice(separator + 2);
              separator = buffer.indexOf('\n\n');
              if (!event) continue;
              if (event.type === 'error') {
                subscriber.error(new HttpErrorResponse({ status: event.data.status, error: { message: event.data.message }, url }));
                return;
              }
              subscriber.next(event);
              if (event.type === 'done') { subscriber.complete(); return; }
            }
          }
          subscriber.error(new HttpErrorResponse({ status: 0, error: null, url }));
        } catch {
          if (!controller.signal.aborted) subscriber.error(new HttpErrorResponse({ status: 0, error: null, url }));
        }
      })();
      return () => controller.abort();
    });
  }

  confirmAction(conversationId: string, actionId: string): Observable<AiActionResolution> {
    return this.http.post<ApiResponse<AiActionResolution>>(
      `${this.baseUrl}/${encodeURIComponent(conversationId)}/actions/${encodeURIComponent(actionId)}/confirm`, {}, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  cancelAction(conversationId: string, actionId: string): Observable<AiActionResolution> {
    return this.http.post<ApiResponse<AiActionResolution>>(
      `${this.baseUrl}/${encodeURIComponent(conversationId)}/actions/${encodeURIComponent(actionId)}/cancel`, {}, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<ApiResponse<object>>(`${this.baseUrl}/${encodeURIComponent(id)}`, { withCredentials: true })
      .pipe(map(() => undefined));
  }

  static errorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409) return error.error?.message ?? 'La consulta está bloqueada o ya se recibió. Actualiza e intenta de nuevo.';
      if (error.status === 403) return error.error?.message ?? 'No tienes autorización para esta consulta.';
      if (error.status === 429) return 'Se alcanzó el límite temporal de consultas. Espera un momento e intenta nuevamente.';
      if (error.status === 503) return error.error?.message ?? 'El proveedor de IA no está disponible temporalmente.';
    }
    return 'No se pudo completar la consulta. La conversación quedó guardada para que puedas revisarla.';
  }
}
