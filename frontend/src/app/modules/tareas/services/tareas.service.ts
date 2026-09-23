import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { ApiResponse, EstadoTarea, KanbanColumn, PagedResult, TareaDetalle, TareaFiltros, TareaFormulario, TareaListado, TareaLookups } from '../models/tareas.models';

@Injectable({ providedIn: 'root' })
export class TareasService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/tasks';

  obtenerListado(filtros: TareaFiltros): Observable<PagedResult<TareaListado>> {
    const params = this.buildParams(filtros);
    return this.http.get<ApiResponse<PagedResult<TareaListado>>>(this.endpoint, { params }).pipe(map((r) => r.data));
  }

  obtenerKanban(filtros: TareaFiltros): Observable<KanbanColumn[]> {
    const params = this.buildParams(filtros);
    return this.http.get<ApiResponse<KanbanColumn[]>>(`${this.endpoint}/kanban`, { params }).pipe(map((r) => r.data));
  }

  obtenerDetalle(id: string): Observable<TareaDetalle> {
    return this.http.get<ApiResponse<TareaDetalle>>(`${this.endpoint}/${id}`).pipe(map((r) => r.data));
  }

  obtenerLookups(): Observable<TareaLookups> {
    return this.http.get<ApiResponse<TareaLookups>>(`${this.endpoint}/lookups`).pipe(map((r) => r.data));
  }

  crear(payload: TareaFormulario): Observable<TareaDetalle> {
    return this.http.post<ApiResponse<TareaDetalle>>(this.endpoint, payload).pipe(map((r) => r.data));
  }

  actualizar(id: string, payload: TareaFormulario): Observable<TareaDetalle> {
    return this.http.put<ApiResponse<TareaDetalle>>(`${this.endpoint}/${id}`, payload).pipe(map((r) => r.data));
  }

  actualizarEstado(id: string, status: EstadoTarea, kanbanOrder = 0): Observable<TareaDetalle> {
    return this.http.patch<ApiResponse<TareaDetalle>>(`${this.endpoint}/${id}/status`, { status, kanbanOrder }).pipe(map((r) => r.data));
  }

  eliminar(id: string): Observable<void> {
    return this.http.delete<ApiResponse<object>>(`${this.endpoint}/${id}`).pipe(map(() => undefined));
  }

  private buildParams(filtros: TareaFiltros): HttpParams {
    let params = new HttpParams().set('pageNumber', filtros.pageNumber).set('pageSize', filtros.pageSize);

    if (filtros.search) params = params.set('search', filtros.search);
    if (filtros.projectId) params = params.set('projectId', filtros.projectId);
    if (filtros.developerId) params = params.set('developerId', filtros.developerId);
    else if (filtros.responsibleId) params = params.set('responsibleId', filtros.responsibleId);
    if (filtros.status) params = params.set('status', filtros.status);
    if (filtros.priority) params = params.set('priority', filtros.priority);
    if (filtros.dueDateFrom) params = params.set('dueDateFrom', filtros.dueDateFrom);
    if (filtros.dueDateTo) params = params.set('dueDateTo', filtros.dueDateTo);

    return params;
  }
}
