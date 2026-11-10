import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';

import { FinanceChartComponent } from '../vision-financiera/finance-chart.component';
import { FinanceChartModel } from '../vision-financiera/vision.models';
import { AiEntityReference, AiMessage } from './intelligent-office.models';

const REFERENCE_LINKS: Record<string, { icon: string; link: string }> = {
  Tarea: { icon: 'task_alt', link: '/tareas' },
  Reunión: { icon: 'event', link: '/agenda' },
  Proyecto: { icon: 'folder_open', link: '/proyectos' },
  Cliente: { icon: 'business', link: '/clientes' },
  Usuario: { icon: 'person', link: '/equipo' },
  Desarrollador: { icon: 'engineering', link: '/equipo' }
};

const SERIES_COLORS = ['#64748b', '#22c55e'];

/** Complementos de una respuesta del asistente: tarjetas de resultados, gráfico y valoración 👍/👎. */
@Component({
  selector: 'app-ai-message-extras',
  standalone: true,
  imports: [FinanceChartComponent, MatIconModule, MatTooltipModule, RouterLink],
  template: `
    @if (charts.length) {
      <div class="charts">
        @if (message.visual?.title) { <span class="charts-title">{{ message.visual!.title }}</span> }
        @for (chart of charts; track chart.currency) {
          <div class="chart"><span class="chart-currency">{{ chart.currency }}</span><app-finance-chart [model]="chart" /></div>
        }
      </div>
    }
    @if (references.length) {
      <div class="results" aria-label="Resultados consultados">
        @for (reference of references; track reference.id) {
          <a class="result" [routerLink]="link(reference)" [matTooltip]="reference.kind + ': abrir ' + link(reference)">
            <mat-icon>{{ icon(reference) }}</mat-icon><span class="result-kind">{{ reference.kind }}</span><span class="result-name">{{ reference.name }}</span>
          </a>
        }
      </div>
    }
    <div class="feedback" role="group" aria-label="¿Te sirvió esta respuesta?">
      <button type="button" class="feedback-up" [class.active]="message.feedback === 1" [attr.aria-pressed]="message.feedback === 1"
        matTooltip="Me sirvió" (click)="rate.emit(message.feedback === 1 ? 0 : 1)"><mat-icon>thumb_up</mat-icon></button>
      <button type="button" class="feedback-down" [class.active]="message.feedback === -1" [attr.aria-pressed]="message.feedback === -1"
        matTooltip="No me sirvió" (click)="rate.emit(message.feedback === -1 ? 0 : -1)"><mat-icon>thumb_down</mat-icon></button>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .charts { display: grid; gap: 8px; margin-top: 10px; }
    .charts-title { color: var(--text-300); font-size: 11px; text-transform: uppercase; letter-spacing: .8px; }
    .chart { padding: 8px; border-radius: 10px; background: rgba(255,255,255,.02); border: 1px solid var(--kd-border-subtle); }
    .chart-currency { font-size: 12px; font-weight: 600; color: var(--text-100); }
    .chart ::ng-deep .chart-frame { height: 180px; }
    .results { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
    .result { display: inline-flex; align-items: center; gap: 5px; max-width: 100%; padding: 5px 10px; border-radius: 10px; font-size: 12px; text-decoration: none;
      color: var(--text-100); background: rgba(255,255,255,.04); border: 1px solid var(--kd-border-subtle); }
    .result:hover { border-color: rgba(34, 197, 94, .45); }
    .result mat-icon { width: 15px; height: 15px; font-size: 15px; color: var(--brand-300); }
    .result-kind { color: var(--text-300); }
    .result-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 220px; }
    .feedback { display: flex; gap: 2px; margin-top: 6px; }
    .feedback button { display: grid; place-items: center; width: 26px; height: 26px; padding: 0; border: 0; border-radius: 6px; background: transparent; color: var(--text-300); cursor: pointer; opacity: .6; }
    .feedback button:hover, .feedback button.active { opacity: 1; background: rgba(255,255,255,.06); }
    .feedback button.active { color: var(--brand-300); }
    .feedback mat-icon { width: 15px; height: 15px; font-size: 15px; }
  `]
})
export class AiMessageExtrasComponent implements OnChanges {
  @Input({ required: true }) message!: AiMessage;
  /** 1 = me sirvió, -1 = no me sirvió, 0 = quitar valoración. */
  @Output() readonly rate = new EventEmitter<number>();

  charts: (FinanceChartModel & { currency: string })[] = [];

  get references(): AiEntityReference[] { return this.message.references ?? []; }

  ngOnChanges(): void {
    const groups = this.message.visual?.groups ?? [];
    this.charts = groups.map(group => ({
      type: 'bar' as const,
      labels: group.labels,
      currency: group.currency,
      description: `${this.message.visual?.title ?? 'Ingresos y gastos'} en ${group.currency}`,
      series: group.datasets.map((dataset, index) => ({
        label: dataset.label,
        values: dataset.values,
        color: SERIES_COLORS[group.datasets.length === 1 ? 1 : index % SERIES_COLORS.length]
      }))
    }));
  }

  icon(reference: AiEntityReference): string { return REFERENCE_LINKS[reference.kind]?.icon ?? 'link'; }

  link(reference: AiEntityReference): string { return REFERENCE_LINKS[reference.kind]?.link ?? '/oficina-inteligente'; }
}
