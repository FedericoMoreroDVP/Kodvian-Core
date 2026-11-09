import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';

import { ApiResponse } from '../../core/auth/auth.models';
import { AiConversation, AiConversationDetail, AiSendResult, Paged } from './intelligent-office.models';

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
