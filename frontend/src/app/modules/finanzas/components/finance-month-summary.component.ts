import { CurrencyPipe } from '@angular/common';
import { Component, Input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { CurrencyPeriod } from '../models/finance-overview.models';

@Component({
  selector: 'app-finance-month-summary', standalone: true, imports: [CurrencyPipe, MatButtonModule],
  template: `
    <section aria-label="Resumen financiero mensual">
      <header><h3>Resumen del mes actual</h3>
        <div role="group" aria-label="Moneda del resumen">
          @for (code of ['ARS', 'USD']; track code) {
            <button mat-button type="button" [attr.aria-pressed]="currency === code" [class.selected]="currency === code" (click)="currency = code">{{ code }}</button>
          }
        </div>
      </header>
      <div class="metrics" [attr.aria-busy]="loading">
        <article><span>Ingresos cobrados del mes</span><strong>{{ !loading && row ? (row.income | currency:currency:'code':'1.2-2') : '—' }}</strong></article>
        <article><span>Gastos pagados del mes</span><strong>{{ !loading && row ? (row.expense | currency:currency:'code':'1.2-2') : '—' }}</strong></article>
        <article><span>Resultado del mes</span><strong [class.negative]="!loading && row && row.result < 0">{{ !loading && row ? (row.result | currency:currency:'code':'1.2-2') : '—' }}</strong></article>
      </div>
      <p>Los movimientos de meses anteriores se consultan en Histórico financiero. Los filtros del listado no modifican este resumen mensual.</p>
    </section>`,
  styles: [`
    header { display:flex; align-items:center; justify-content:space-between; gap:12px; margin-bottom:10px; }
    h3 { margin:0; font-size:15px; } header button { min-width:54px; }
    .selected { background:rgba(24,189,117,.12); color:var(--brand-300); }
    .metrics { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:12px; }
    article { padding:14px 16px; border:1px solid var(--kd-border-subtle); border-radius:12px; background:var(--kd-surface,#111c25); }
    span { display:block; font-size:12px; color:var(--kd-text-secondary); }
    strong { display:block; margin-top:7px; font-size:22px; overflow-wrap:anywhere; }
    .negative { color:#ff9b9b; } p { font-size:12px; color:var(--kd-text-muted); margin:10px 0 0; }
    @media(max-width:700px) { .metrics { grid-template-columns:1fr; } strong { font-size:20px; } }
  `]
})
export class FinanceMonthSummaryComponent {
  @Input() rows: CurrencyPeriod[] = [];
  @Input() loading = false;
  currency = 'ARS';
  get row(): CurrencyPeriod | undefined { return this.rows.find(x => x.currency === this.currency); }
}
