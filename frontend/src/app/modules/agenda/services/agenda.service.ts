import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { forkJoin, map, Observable, of, switchMap } from 'rxjs';
import { ApiResponse } from '../../../shared/models/api.models';
import { AgendaFiltros, AgendaLookups, AgendaResultado, Reunion, ReunionFormulario } from '../models/agenda.models';

@Injectable({ providedIn: 'root' })
export class AgendaService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/meetings';

  obtenerReuniones(filtros: AgendaFiltros): Observable<AgendaResultado> {
    const params = this.buildParams(filtros);
    return this.http.get<ApiResponse<AgendaResultado>>(this.endpoint, { params }).pipe(map(r => r.data));
  }

  obtenerTodasLasReuniones(filtros: Omit<AgendaFiltros, 'pageNumber' | 'pageSize'>): Observable<Reunion[]> {
    const pageSize = 100;
    return this.obtenerReuniones({ ...filtros, pageNumber: 1, pageSize }).pipe(switchMap(first => {
      const remainingPages = Math.ceil(first.totalCount / pageSize) - 1;
      if (remainingPages <= 0) return of(first.items);
      return forkJoin(Array.from({ length: remainingPages }, (_, index) => this.obtenerReuniones({ ...filtros, pageNumber: index + 2, pageSize }))).pipe(
        map(pages => first.items.concat(...pages.map(page => page.items)))
      );
    }));
  }

  private buildParams(filtros: AgendaFiltros): HttpParams {
    let params = new HttpParams().set('pageNumber', filtros.pageNumber).set('pageSize', filtros.pageSize).set('from', filtros.from).set('to', filtros.to);
    if (filtros.projectId) params = params.set('projectId', filtros.projectId);
    if (filtros.priority) params = params.set('priority', filtros.priority);
    if (filtros.status) params = params.set('status', filtros.status);
    return params;
  }

  obtenerDetalle(id: string): Observable<Reunion> {
    return this.http.get<ApiResponse<Reunion>>(`${this.endpoint}/${id}`).pipe(map(r => r.data));
  }

  obtenerLookups(): Observable<AgendaLookups> {
    return this.http.get<ApiResponse<AgendaLookups>>(`${this.endpoint}/lookups`).pipe(map(r => r.data));
  }

  crear(payload: ReunionFormulario): Observable<Reunion> {
    return this.http.post<ApiResponse<Reunion>>(this.endpoint, payload).pipe(map(r => r.data));
  }

  actualizar(id: string, payload: ReunionFormulario): Observable<Reunion> {
    return this.http.put<ApiResponse<Reunion>>(`${this.endpoint}/${id}`, payload).pipe(map(r => r.data));
  }

  cancelar(id: string): Observable<Reunion> {
    return this.http.patch<ApiResponse<Reunion>>(`${this.endpoint}/${id}/cancel`, {}).pipe(map(r => r.data));
  }
}
