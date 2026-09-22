import { TestBed } from '@angular/core/testing';
import { FinanceChartComponent } from './finance-chart.component';
import type { BarElement } from 'chart.js';

describe('Gráfico financiero interactivo', () => {
  it('renderiza Chart.js, permite seleccionar una barra y libera el gráfico al salir', async () => {
    const fixture = TestBed.createComponent(FinanceChartComponent);
    fixture.nativeElement.style.display = 'block'; fixture.nativeElement.style.width = '650px';
    fixture.componentInstance.model = { type: 'bar', labels: ['may 25', 'jun 25'], currency: 'ARS', description: 'Cobros por mes',
      series: [{ label: 'Cobrado', values: [100, 200], color: '#4dd3ac' }] };
    const selected = jasmine.createSpy(); fixture.componentInstance.selected.subscribe(selected);
    fixture.detectChanges(); await fixture.whenStable();
    const { Chart } = await import('./chart-runtime');
    await new Promise(resolve => setTimeout(resolve, 50));
    const canvas = fixture.nativeElement.querySelector('canvas') as HTMLCanvasElement;
    const chart = Chart.getChart(canvas)!; expect(chart).toBeTruthy();
    chart.resize(); chart.update('none');
    const point = (chart.getDatasetMeta(0).data[0] as BarElement).getCenterPoint(); const rect = canvas.getBoundingClientRect();
    if (point.x === null || point.y === null) throw new Error('La barra debe tener coordenadas renderizadas');
    canvas.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: rect.left + point.x, clientY: rect.top + point.y }));
    await new Promise(resolve => setTimeout(resolve, 50));
    expect(selected).toHaveBeenCalledWith({ index: 0, datasetIndex: 0 });
    expect(canvas.getAttribute('aria-label')).toBe('Cobros por mes'); fixture.destroy();
    expect(Chart.getChart(canvas)).toBeUndefined();
  });
});
