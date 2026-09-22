import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';
import { ApiResponse, PagedResult } from '../../../shared/models/api.models';
import { LedgerContrato } from '../../proyectos/models/proyectos.models';
import { FinanceOverview, FinanceSetup, Partner } from '../models/finance-overview.models';

@Injectable({ providedIn: 'root' })
export class FinanceOverviewService {
  private readonly http = inject(HttpClient);
  teamObligations(year: number, pageNumber: number) {
    return this.http.get<ApiResponse<PagedResult<LedgerContrato>>>('/api/finance/team-obligations', { params: { year, pageNumber, pageSize: 10 } }).pipe(map(r => r.data));
  }
  overview(from = '', to = '') {
    let params = new HttpParams(); if (from) params = params.set('from', from); if (to) params = params.set('to', to);
    return this.http.get<ApiResponse<FinanceOverview>>('/api/finance/overview', { params }).pipe(map(r => r.data));
  }
  setup(value: FinanceSetup) { return this.http.put<ApiResponse<FinanceSetup>>('/api/finance/setup', value).pipe(map(r => r.data)); }
  partners() { return this.http.get<ApiResponse<Partner[]>>('/api/finance/partners').pipe(map(r => r.data)); }
  savePartner(fullName: string, isActive = true, id?: string) {
    return (id ? this.http.put<ApiResponse<Partner>>(`/api/finance/partners/${id}`, { fullName, isActive })
      : this.http.post<ApiResponse<Partner>>('/api/finance/partners', { fullName, isActive })).pipe(map(r => r.data));
  }
  exchange(value: { requestId: string; fromCurrency: string; toCurrency: string; fromAmount: number; toAmount: number; date: string; notes: string }) {
    return this.http.post<ApiResponse<string>>('/api/finance/exchanges', value).pipe(map(r => r.data));
  }
  cancelExchange(id: string) { return this.http.delete(`/api/finance/exchanges/${id}`); }
}
