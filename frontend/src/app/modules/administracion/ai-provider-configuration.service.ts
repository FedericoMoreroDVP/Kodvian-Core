import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';

import { ApiResponse } from '../../core/auth/auth.models';
import { AiProviderConfiguration, AiProviderConnectionTest } from './ai-provider-configuration.models';

@Injectable({ providedIn: 'root' })
export class AiProviderConfigurationService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/ai/provider-configuration';

  get(): Observable<AiProviderConfiguration> {
    return this.http.get<ApiResponse<AiProviderConfiguration>>(this.url, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  save(request: { provider: string; model: string; fastModel?: string | null; apiKey: string; version: string }): Observable<AiProviderConfiguration> {
    return this.http.put<ApiResponse<AiProviderConfiguration>>(this.url, request, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  test(): Observable<AiProviderConnectionTest> {
    return this.http.post<ApiResponse<AiProviderConnectionTest>>(`${this.url}/test`, {}, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  approveLiveData(termsVersion: string): Observable<AiProviderConfiguration> {
    return this.http.post<ApiResponse<AiProviderConfiguration>>(`${this.url}/approve-live-data`,
      { acknowledgeTerms: true, termsVersion }, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  revokeLiveDataApproval(): Observable<AiProviderConfiguration> {
    return this.http.delete<ApiResponse<AiProviderConfiguration>>(`${this.url}/approve-live-data`, { withCredentials: true })
      .pipe(map(response => response.data));
  }

  remove(version: string): Observable<void> {
    return this.http.delete<ApiResponse<object>>(`${this.url}?expectedVersion=${encodeURIComponent(version)}`, { withCredentials: true })
      .pipe(map(() => undefined));
  }
}
