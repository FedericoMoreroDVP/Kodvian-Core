import { Component, Inject, OnDestroy, OnInit, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Subscription } from 'rxjs';
import { FinanzasService } from '../finanzas/services/finanzas.service';
import { FinanzaFiltros, MovimientoListado } from '../finanzas/models/finanzas.models';
import { dateLabel, money } from './vision.models';

export interface MovementDetailData { title: string; subtitle: string; amount: number; currency: string; filters: Omit<FinanzaFiltros, 'pageNumber' | 'pageSize'>; }
@Component({ selector: 'app-vision-movement-detail', standalone: true, imports: [MatDialogModule, MatButtonModule, MatPaginatorModule],
  template: `<h2 mat-dialog-title>{{ data.title }}</h2><mat-dialog-content>
    <p class="subtitle">{{ data.subtitle }}</p><strong class="total">{{ money(data.amount, data.currency) }}</strong><p class="subtitle">Importe del indicador consultado. Los movimientos se consultan al abrir el detalle.</p>
    @if (error) { <p role="alert">{{ error }} <button mat-button (click)="load()">Reintentar</button></p> }
    @if (loading) { <p role="status">Consultando movimientos…</p> }
    <div class="table-scroll"><table><thead><tr><th>Fecha</th><th>Concepto</th><th>Categoría</th><th>Estado</th><th>Importe</th><th>Impacto en indicador</th></tr></thead><tbody>
      @for (row of rows; track row.id) { <tr><td>{{ dateLabel(data.filters.view?.startsWith('Pending') ? row.movementDate : row.settlementDate) }}</td><td>{{ row.description }}</td><td>{{ row.categoryName }}</td><td>{{ row.status }}</td><td>{{ money(row.amount, row.currency) }}</td><td [class.negative]="(row.indicatorAmount ?? 0) < 0">{{ money(row.indicatorAmount ?? row.amount, row.currency) }}</td></tr> }
      @empty { @if (!loading && !error) { <tr><td colspan="6">No hay movimientos para esta selección.</td></tr> } }
    </tbody></table></div>
    <mat-paginator [length]="total" [pageSize]="pageSize" [pageIndex]="page - 1" [pageSizeOptions]="[10,20,50]" [disabled]="loading" (page)="paginate($event)" />
    </mat-dialog-content><mat-dialog-actions align="end"><button mat-flat-button mat-dialog-close>Volver a Visión financiera</button></mat-dialog-actions>`,
  styles: [`.subtitle{color:var(--kd-text-secondary);font-size:13px}.total{font-size:26px}.table-scroll{overflow:auto;margin-top:18px}table{border-collapse:collapse;width:100%}td,th{padding:12px;text-align:left;border-bottom:1px solid var(--kd-border-subtle)}th{font-size:12px;color:var(--kd-text-secondary)}td{font-size:13px}.negative,[role=alert]{color:#ffaba1}`]
})
export class MovementDetailDialogComponent implements OnInit, OnDestroy {
  private readonly api = inject(FinanzasService);
  private request?: Subscription;
  rows: MovimientoListado[] = []; total = 0; page = 1; pageSize = 10; loading = false; error = '';
  readonly money = money; readonly dateLabel = dateLabel;
  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: MovementDetailData) {}
  ngOnInit(): void { this.load(); }
  ngOnDestroy(): void { this.request?.unsubscribe(); }
  load(): void {
    this.request?.unsubscribe(); this.loading = true; this.error = ''; this.rows = [];
    this.request = this.api.obtenerMovimientos({ ...this.data.filters, currency: this.data.currency, pageNumber: this.page, pageSize: this.pageSize }).subscribe({
      next: result => { this.rows = result.items; this.total = result.totalCount; this.loading = false; },
      error: () => { this.error = 'No se pudieron consultar los movimientos'; this.loading = false; }
    });
  }
  paginate(event: PageEvent): void { this.page = event.pageIndex + 1; this.pageSize = event.pageSize; this.load(); }
}
