import { Component, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { FinanceOverviewComponent } from './finance-overview.component';

export interface FinanceHistoryDetail { currency: string; movementType: 'Ingreso' | 'Egreso'; from: string; to: string; }
export interface FinanceHistoryDialogResult { detail?: FinanceHistoryDetail; }

@Component({
  selector: 'app-finance-overview-dialog', standalone: true,
  imports: [MatDialogModule, MatButtonModule, FinanceOverviewComponent],
  template: `
    <h2 mat-dialog-title>Histórico financiero</h2>
    <mat-dialog-content><app-finance-overview (detail)="viewDetail($event)" (busyChange)="setBusy($event)" /></mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-flat-button type="button" [disabled]="busy" (click)="close()">Cerrar</button></mat-dialog-actions>`,
  styles: [`mat-dialog-content { max-height:72vh; } @media(max-width:700px) { mat-dialog-content { padding:0 12px; } }`]
})
export class FinanceOverviewDialogComponent {
  private readonly ref = inject(MatDialogRef<FinanceOverviewDialogComponent, FinanceHistoryDialogResult>);
  busy = false;
  setBusy(value: boolean): void { this.busy = value; this.ref.disableClose = value; }
  close(): void { if (!this.busy) this.ref.close({}); }
  viewDetail(detail: FinanceHistoryDetail): void { if (!this.busy) this.ref.close({ detail }); }
}
