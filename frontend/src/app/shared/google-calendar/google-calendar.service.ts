import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';

import { ApiResponse } from '../../core/auth/auth.models';

export interface GoogleCalendarStatus {
  /** False si el servidor todavía no tiene configurado el cliente OAuth de Google. */
  enabled: boolean;
  connected: boolean;
  email: string | null;
  connectedAtUtc: string | null;
  lastSyncAtUtc: string | null;
  lastError: string | null;
}

export interface GoogleCalendarSyncResult {
  status: 'Synced' | 'Failed' | null;
  error: string | null;
}

/** Conexión de la cuenta de Google del usuario para agendar sus reuniones en Google Calendar. */
@Injectable({ providedIn: 'root' })
export class GoogleCalendarService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/integrations/google-calendar';

  status(): Observable<GoogleCalendarStatus> {
    return this.http.get<ApiResponse<GoogleCalendarStatus>>(`${this.baseUrl}/status`, { withCredentials: true }).pipe(map(response => response.data));
  }

  /** Devuelve la URL de autorización de Google; la pantalla redirige el navegador a esa URL. */
  connect(): Observable<string> {
    return this.http.post<ApiResponse<{ authorizationUrl: string }>>(`${this.baseUrl}/connect`, {}, { withCredentials: true })
      .pipe(map(response => response.data.authorizationUrl));
  }

  disconnect(): Observable<void> {
    return this.http.delete<ApiResponse<object>>(this.baseUrl, { withCredentials: true }).pipe(map(() => undefined));
  }

  retry(meetingId: string): Observable<GoogleCalendarSyncResult> {
    return this.http.post<ApiResponse<GoogleCalendarSyncResult>>(`${this.baseUrl}/meetings/${encodeURIComponent(meetingId)}/sync`, {}, { withCredentials: true })
      .pipe(map(response => response.data));
  }
}
