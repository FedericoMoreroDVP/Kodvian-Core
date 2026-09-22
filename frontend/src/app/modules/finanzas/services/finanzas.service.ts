import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import {
  ApiResponse,
  ComprobanteArchivo,
  CategoriaFinanciera,
  CategoriaFormulario,
  FinanzasLookups,
  FinanzaFiltros,
  MovimientoDetalle,
  MovimientoFormulario,
  MovimientoListado,
  PagedResult,
  Proveedor,
  ResumenMensual
} from '../models/finanzas.models';

@Injectable({ providedIn: 'root' })
export class FinanzasService {
  private readonly http = inject(HttpClient);

  obtenerMovimientos(filtros: FinanzaFiltros): Observable<PagedResult<MovimientoListado>> {
    let params = new HttpParams().set('pageNumber', filtros.pageNumber).set('pageSize', filtros.pageSize);
    if (filtros.view) params = params.set('view', filtros.view);
    if (filtros.partnerId) params = params.set('partnerId', filtros.partnerId);
    if (filtros.currency) params = params.set('currency', filtros.currency);
    if (filtros.nature) params = params.set('nature', filtros.nature);
    if (filtros.projectId) params = params.set('projectId', filtros.projectId);
    if (filtros.unlinkedOnly) params = params.set('unlinkedOnly', true);
    if (filtros.useSettlementDate) params = params.set('useSettlementDate', true);
    if (filtros.exactAmount) params = params.set('exactAmount', filtros.exactAmount);
    if (filtros.dateFrom) params = params.set('dateFrom', filtros.dateFrom);
    if (filtros.dateTo) params = params.set('dateTo', filtros.dateTo);
    if (filtros.movementType) params = params.set('movementType', filtros.movementType);
    if (filtros.categoryId) params = params.set('categoryId', filtros.categoryId);
    if (filtros.clientId) params = params.set('clientId', filtros.clientId);
    if (filtros.providerId) params = params.set('providerId', filtros.providerId);
    if (filtros.status) params = params.set('status', filtros.status);

    return this.http.get<ApiResponse<PagedResult<MovimientoListado>>>('/api/financial-movements', { params }).pipe(map((r) => r.data));
  }

  obtenerDetalle(id: string): Observable<MovimientoDetalle> {
    return this.http.get<ApiResponse<MovimientoDetalle>>(`/api/financial-movements/${id}`).pipe(map((r) => r.data));
  }

  crearMovimiento(payload: MovimientoFormulario): Observable<MovimientoDetalle> {
    return this.http.post<ApiResponse<MovimientoDetalle>>('/api/financial-movements', payload).pipe(map((r) => r.data));
  }

  actualizarMovimiento(id: string, payload: MovimientoFormulario): Observable<MovimientoDetalle> {
    return this.http.put<ApiResponse<MovimientoDetalle>>(`/api/financial-movements/${id}`, payload).pipe(map((r) => r.data));
  }

  obtenerResumenMensual(year?: number, month?: number): Observable<ResumenMensual> {
    let params = new HttpParams();
    if (year) params = params.set('year', year);
    if (month) params = params.set('month', month);

    return this.http.get<ApiResponse<ResumenMensual>>('/api/financial-movements/monthly-summary', { params }).pipe(map((r) => r.data));
  }

  obtenerLookups(): Observable<FinanzasLookups> {
    return this.http.get<ApiResponse<FinanzasLookups>>('/api/financial-movements/lookups').pipe(map((r) => r.data));
  }

  obtenerCategorias(): Observable<CategoriaFinanciera[]> {
    return this.http.get<ApiResponse<CategoriaFinanciera[]>>('/api/financial-categories').pipe(map((r) => r.data));
  }

  crearCategoria(payload: CategoriaFormulario): Observable<CategoriaFinanciera> {
    return this.http.post<ApiResponse<CategoriaFinanciera>>('/api/financial-categories', payload).pipe(map((r) => r.data));
  }

  actualizarCategoria(id: string, payload: CategoriaFormulario): Observable<CategoriaFinanciera> {
    return this.http.put<ApiResponse<CategoriaFinanciera>>(`/api/financial-categories/${id}`, payload).pipe(map((r) => r.data));
  }

  obtenerProveedores(): Observable<Proveedor[]> {
    return this.http.get<ApiResponse<Proveedor[]>>('/api/providers').pipe(map((r) => r.data));
  }

  subirComprobanteMovimiento(movementId: string, file: File): Observable<ComprobanteArchivo> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<ApiResponse<ComprobanteArchivo>>(`/api/financial-movements/${movementId}/receipts`, formData).pipe(map((r) => r.data));
  }
}
