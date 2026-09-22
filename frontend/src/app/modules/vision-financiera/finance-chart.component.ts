import { AfterViewInit, Component, ElementRef, EventEmitter, Input, NgZone, OnChanges, OnDestroy, Output, ViewChild, inject } from '@angular/core';
import type { Chart, ChartConfiguration } from 'chart.js';
import { ChartSelection, FinanceChartModel, money } from './vision.models';

@Component({ selector: 'app-finance-chart', standalone: true,
  template: `<div class="chart-frame"><canvas #canvas role="img" [attr.aria-label]="model.description">Consulta la tabla accesible que acompaña al gráfico.</canvas></div>
    @if (error) { <p role="alert">No se pudo cargar el gráfico. Puedes consultar sus datos en la tabla.</p> }`,
  styles: [`.chart-frame { position:relative; height:280px; min-width:0; } canvas { max-width:100%; } p { color:#ffb0a1; font-size:13px; } @media(max-width:600px){.chart-frame{height:230px}}`]
})
export class FinanceChartComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input({ required: true }) model!: FinanceChartModel;
  @Output() selected = new EventEmitter<ChartSelection>();
  @ViewChild('canvas') canvas?: ElementRef<HTMLCanvasElement>;
  private readonly zone = inject(NgZone);
  private chart?: Chart<'bar' | 'line', number[], string>;
  private revision = 0;
  private destroyed = false;
  error = false;
  ngAfterViewInit(): void { void this.render(); }
  ngOnChanges(): void { if (this.canvas) void this.render(); }
  ngOnDestroy(): void { this.destroyed = true; this.revision++; this.chart?.destroy(); }

  private async render(): Promise<void> {
    const revision = ++this.revision;
    try {
      const { Chart } = await import('./chart-runtime');
      if (this.destroyed || revision !== this.revision || !this.canvas) return;
      const model = this.model;
      const config: ChartConfiguration<'bar' | 'line', number[], string> = {
        type: model.type,
        data: { labels: model.labels, datasets: model.series.map(series => ({
          label: series.label, data: series.values, backgroundColor: model.type === 'line' ? series.color : series.color + 'bb',
          borderColor: series.color, borderWidth: 2, borderRadius: 5, pointRadius: 3, pointHoverRadius: 6, tension: 0.15, maxBarThickness: 38
        })) },
        options: {
          responsive: true, maintainAspectRatio: false, animation: false, indexAxis: model.horizontal ? 'y' : 'x',
          interaction: { mode: 'nearest', intersect: true },
          onClick: (_event, elements) => {
            const element = elements[0];
            if (element) this.zone.run(() => this.selected.emit({ index: element.index, datasetIndex: element.datasetIndex }));
          },
          plugins: {
            legend: { display: model.series.length > 1, position: 'bottom', labels: { color: '#becbd6', usePointStyle: true, padding: 22 } },
            tooltip: { backgroundColor: '#1b2938', titleColor: '#f1f5f9', bodyColor: '#e2e8f0', padding: 12,
              callbacks: {
                title: contexts => { const i = contexts[0]?.dataIndex; return i == null ? '' : (model.fullLabels ?? model.labels)[i]; },
                label: context => `${context.dataset.label}: ${money(Number(context.raw), model.currency)}`
              } }
          },
          scales: {
            x: { grid: { color: '#263544', display: !!model.horizontal }, ticks: { color: '#a6b7c8', maxRotation: 0,
              ...(model.horizontal ? { callback: (value: string | number) => new Intl.NumberFormat('es-AR', { notation: 'compact' }).format(Number(value)) } : {}) } },
            y: { beginAtZero: true, grid: { color: '#263544', display: !model.horizontal }, ticks: { color: '#a6b7c8',
              ...(!model.horizontal ? { callback: (value: string | number) => new Intl.NumberFormat('es-AR', { notation: 'compact' }).format(Number(value)) } : {}) } }
          }
        }
      };
      this.zone.runOutsideAngular(() => { this.chart?.destroy(); this.chart = new Chart(this.canvas!.nativeElement, config); });
      this.error = false;
    } catch { if (!this.destroyed && revision === this.revision) this.error = true; }
  }
}
