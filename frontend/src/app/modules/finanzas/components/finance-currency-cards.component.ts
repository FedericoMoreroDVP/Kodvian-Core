import { CurrencyPipe } from '@angular/common';
import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CurrencyOverview } from '../models/finance-overview.models';

@Component({ selector: 'app-finance-currency-cards', standalone: true, imports: [CurrencyPipe], template: `
  <div class="currencies">
    @for (row of rows; track row.currency) {
      <section class="currency"><h3>{{ row.currency === 'ARS' ? 'Pesos argentinos · ARS' : 'Dólares · USD' }}</h3>
        <dl>
          <div><dt>Ingresos cobrados</dt><dd>{{ row.income | currency:row.currency:'code':'1.2-2' }}</dd>
            @if (showDetails) { <button type="button" (click)="detail.emit({currency: row.currency, movementType: 'Ingreso'})">Ver cobros</button> }</div>
          <div><dt>Gastos pagados</dt><dd>{{ row.expense | currency:row.currency:'code':'1.2-2' }}</dd>
            @if (showDetails) { <button type="button" (click)="detail.emit({currency: row.currency, movementType: 'Egreso'})">Ver gastos</button> }</div>
          <div><dt>Resultado operativo de caja</dt><dd [class.negative]="row.result < 0">{{ row.result | currency:row.currency:'code':'1.2-2' }}</dd></div>
          <div><dt>Aportes de socios (incluye gastos aportados)</dt><dd>{{ row.contributions | currency:row.currency:'code':'1.2-2' }}</dd></div>
          <div><dt>Retiros de socios</dt><dd>{{ row.withdrawals | currency:row.currency:'code':'1.2-2' }}</dd></div>
          <div><dt>Egresos registrados por pagar</dt><dd>{{ row.pendingExpense | currency:row.currency:'code':'1.2-2' }}</dd></div>
          <div><dt>Ingresos registrados por cobrar</dt><dd>{{ row.pendingIncome | currency:row.currency:'code':'1.2-2' }}</dd></div>
          <div><dt>Variación de caja del período</dt><dd>{{ row.cashChange | currency:row.currency:'code':'1.2-2' }}</dd></div>
          <div class="balance"><dt>Saldo de caja estimado al corte {{ provisional ? '(provisional)' : '' }}</dt><dd>{{ row.balance === null ? 'Punto de partida por configurar' : (row.balance | currency:row.currency:'code':'1.2-2') }}</dd></div>
        </dl>
      </section>
    }
  </div>`, styles: [`
  .currencies { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 14px; }
  .currency { padding: 18px; border: 1px solid var(--kd-border-subtle); border-radius: 12px; background: var(--kd-surface, #111c25); }
  h3 { margin-top: 0; } dl { margin: 0; display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 14px; }
  dt { font-size: 12px; opacity: .8; } dd { margin: 5px 0 0; font-size: 17px; overflow-wrap: anywhere; } .balance { grid-column: 1/-1; }
  .negative { color: #ff9b9b; } button { color: var(--brand-300, #18bf76); background: none; border: 0; padding: 5px 0; cursor: pointer; }
  @media(max-width:900px) { .currencies { grid-template-columns: 1fr; } }
  `] })
export class FinanceCurrencyCardsComponent {
  @Input() rows: CurrencyOverview[] = [];
  @Input() provisional = true;
  @Input() showDetails = false;
  @Output() detail = new EventEmitter<{currency: string; movementType: 'Ingreso' | 'Egreso'}>();
}
