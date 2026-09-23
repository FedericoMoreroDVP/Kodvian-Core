import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ProjectDriveLink } from '../models/project-drive-link.model';

import {
  ApiResponse,
  AsignacionDesarrolladorFormulario,
  AsignacionDesarrolladorProyecto,
  ContratoDesarrollador,
  ContratoDesarrolladorFormulario,
  DocumentoProyecto,
  DesarrolladorFormulario,
  DesarrolladorExterno,
  LedgerContrato,
  PagoDesarrollador,
  PagoDesarrolladorFormulario,
  PagedResult,
  ProyectoDetalle,
  ProyectoFiltros,
  ProyectoFormulario,
  ProyectoListado,
  ProyectoLookups,
  TipoDocumentoProyecto,
  TipoDocumentoProyectoItem,
  VersionDocumentoProyecto
} from '../models/proyectos.models';

@Injectable({
  providedIn: 'root'
})
export class ProyectosService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/projects';

  obtenerListado(filtros: ProyectoFiltros): Observable<PagedResult<ProyectoListado>> {
    let params = new HttpParams()
      .set('pageNumber', filtros.pageNumber)
      .set('pageSize', filtros.pageSize);

    if (filtros.search) {
      params = params.set('search', filtros.search);
    }

    if (filtros.clientId) {
      params = params.set('clientId', filtros.clientId);
    }

    if (filtros.status) {
      params = params.set('status', filtros.status);
    }

    if (filtros.priority) {
      params = params.set('priority', filtros.priority);
    }

    return this.http.get<ApiResponse<PagedResult<ProyectoListado>>>(this.endpoint, { params }).pipe(map((r) => r.data));
  }

  obtenerDetalle(id: string): Observable<ProyectoDetalle> {
    return this.http.get<ApiResponse<ProyectoDetalle>>(`${this.endpoint}/${id}`).pipe(map((r) => r.data));
  }

  obtenerLookups(): Observable<ProyectoLookups> {
    return this.http.get<ApiResponse<ProyectoLookups>>(`${this.endpoint}/lookups`).pipe(map((r) => r.data));
  }

  crear(payload: ProyectoFormulario): Observable<ProyectoDetalle> {
    return this.http.post<ApiResponse<ProyectoDetalle>>(this.endpoint, payload).pipe(map((r) => r.data));
  }

  actualizar(id: string, payload: ProyectoFormulario): Observable<ProyectoDetalle> {
    return this.http.put<ApiResponse<ProyectoDetalle>>(`${this.endpoint}/${id}`, payload).pipe(map((r) => r.data));
  }

  obtenerEnlaceDrive(id: string): Observable<ProjectDriveLink> {
    return this.http.get<ApiResponse<ProjectDriveLink>>(`${this.endpoint}/${id}/drive-link`).pipe(map(r => r.data));
  }

  guardarEnlaceDrive(id: string, googleDriveFolderUrl: string | null): Observable<ProjectDriveLink> {
    return this.http.put<ApiResponse<ProjectDriveLink>>(`${this.endpoint}/${id}/drive-link`, { googleDriveFolderUrl }).pipe(map(r => r.data));
  }

  obtenerDesarrolladores(): Observable<DesarrolladorExterno[]> {
    return this.http.get<ApiResponse<DesarrolladorExterno[]>>('/api/developers').pipe(map((r) => r.data));
  }

  crearDesarrollador(payload: DesarrolladorFormulario): Observable<DesarrolladorExterno> {
    return this.http.post<ApiResponse<DesarrolladorExterno>>('/api/developers', payload).pipe(map((r) => r.data));
  }

  obtenerAsignacionesDesarrollador(projectId: string): Observable<AsignacionDesarrolladorProyecto[]> {
    return this.http.get<ApiResponse<AsignacionDesarrolladorProyecto[]>>(`/api/projects/${projectId}/developer-assignments`).pipe(map((r) => r.data));
  }

  asignarDesarrollador(projectId: string, payload: AsignacionDesarrolladorFormulario): Observable<AsignacionDesarrolladorProyecto> {
    return this.http.post<ApiResponse<AsignacionDesarrolladorProyecto>>(`/api/projects/${projectId}/developer-assignments`, payload).pipe(map((r) => r.data));
  }

  quitarAsignacionDesarrollador(assignmentId: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`/api/project-developer-assignments/${assignmentId}`).pipe(map(() => undefined));
  }

  obtenerContratosDesarrollador(projectId: string): Observable<ContratoDesarrollador[]> {
    return this.http.get<ApiResponse<ContratoDesarrollador[]>>(`/api/projects/${projectId}/developer-contracts`).pipe(map((r) => r.data));
  }

  crearContratoDesarrollador(projectId: string, payload: ContratoDesarrolladorFormulario): Observable<ContratoDesarrollador> {
    return this.http.post<ApiResponse<ContratoDesarrollador>>(`/api/projects/${projectId}/developer-contracts`, payload).pipe(map((r) => r.data));
  }

  actualizarContratoDesarrollador(contractId: string, payload: ContratoDesarrolladorFormulario): Observable<ContratoDesarrollador> {
    return this.http.put<ApiResponse<ContratoDesarrollador>>(`/api/developer-contracts/${contractId}`, payload).pipe(map((r) => r.data));
  }
  eliminarContratoDesarrollador(contractId: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`/api/developer-contracts/${contractId}`).pipe(map(() => undefined));
  }

  obtenerPagosContrato(contractId: string): Observable<PagoDesarrollador[]> {
    return this.http.get<ApiResponse<PagoDesarrollador[]>>(`/api/developer-contracts/${contractId}/payments`).pipe(map((r) => r.data));
  }

  registrarPagoContrato(contractId: string, payload: PagoDesarrolladorFormulario): Observable<PagoDesarrollador> {
    return this.http.post<ApiResponse<PagoDesarrollador>>(`/api/developer-contracts/${contractId}/payments`, payload).pipe(map((r) => r.data));
  }

  actualizarPago(id: string, payload: PagoDesarrolladorFormulario): Observable<PagoDesarrollador> {
    return this.http.put<ApiResponse<PagoDesarrollador>>(`/api/developer-payments/${id}`, payload).pipe(map(r => r.data));
  }
  anularPago(payment: PagoDesarrollador) {
    return this.http.delete(`/api/developer-payments/${payment.id}`, { params: { expectedVersion: payment.version } });
  }

  subirComprobantePago(paymentId: string, file: File): Observable<void> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<ApiResponse<unknown>>(`/api/developer-payments/${paymentId}/receipts`, formData).pipe(map(() => undefined));
  }

  eliminarComprobantePago(paymentId: string, receiptId: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`/api/developer-payments/${paymentId}/receipts/${receiptId}`).pipe(map(() => undefined));
  }

  obtenerLedgerContrato(contractId: string, year: number): Observable<LedgerContrato> {
    const params = new HttpParams().set('year', year);
    return this.http.get<ApiResponse<LedgerContrato>>(`/api/developer-contracts/${contractId}/ledger`, { params }).pipe(map((r) => r.data));
  }

  obtenerDocumentosProyecto(projectId: string): Observable<DocumentoProyecto[]> {
    return this.http.get<ApiResponse<DocumentoProyecto[]>>(`/api/projects/${projectId}/documents`).pipe(map((r) => r.data));
  }

  obtenerTiposDocumentoProyecto(): Observable<TipoDocumentoProyectoItem[]> {
    return this.http.get<ApiResponse<TipoDocumentoProyectoItem[]>>('/api/projects/document-types').pipe(map((r) => r.data));
  }

  subirDocumentoProyecto(projectId: string, payload: { file: File; title: string; type: TipoDocumentoProyecto; notes?: string }): Observable<DocumentoProyecto> {
    const formData = new FormData();
    formData.append('file', payload.file);
    formData.append('title', payload.title);
    formData.append('type', payload.type);
    if (payload.notes?.trim()) {
      formData.append('notes', payload.notes.trim());
    }

    return this.http.post<ApiResponse<DocumentoProyecto>>(`/api/projects/${projectId}/documents`, formData).pipe(map((r) => r.data));
  }

  subirVersionDocumentoProyecto(projectId: string, documentId: string, payload: { file: File; notes?: string }): Observable<DocumentoProyecto> {
    const formData = new FormData();
    formData.append('file', payload.file);
    if (payload.notes?.trim()) {
      formData.append('notes', payload.notes.trim());
    }

    return this.http
      .post<ApiResponse<DocumentoProyecto>>(`/api/projects/${projectId}/documents/${documentId}/versions`, formData)
      .pipe(map((r) => r.data));
  }

  obtenerVersionesDocumentoProyecto(projectId: string, documentId: string): Observable<VersionDocumentoProyecto[]> {
    return this.http
      .get<ApiResponse<VersionDocumentoProyecto[]>>(`/api/projects/${projectId}/documents/${documentId}/versions`)
      .pipe(map((r) => r.data));
  }

  eliminarDocumentoProyecto(projectId: string, documentId: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`/api/projects/${projectId}/documents/${documentId}`).pipe(map(() => undefined));
  }
}
