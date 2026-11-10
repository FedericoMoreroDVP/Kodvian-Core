import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';

import { ApiResponse } from '../../core/auth/auth.models';

export interface AiUsageSettings {
  inputUsdPerMillion: number | null;
  outputUsdPerMillion: number | null;
  fastInputUsdPerMillion: number | null;
  fastOutputUsdPerMillion: number | null;
  monthlyBudgetUsd: number | null;
  updatedAtUtc?: string | null;
}

export interface AiUsageReport {
  from: string;
  to: string;
  totals: { runs: number; failedRuns: number; partialRuns: number; inputTokens: number; outputTokens: number; costUsd: number };
  days: { date: string; runs: number; failedRuns: number; inputTokens: number; outputTokens: number; costUsd: number }[];
  byModel: { provider: string; model: string; runs: number; inputTokens: number; outputTokens: number; costUsd: number }[];
  topTools: { name: string; count: number }[];
  errors: { name: string; count: number }[];
  actions: { name: string; count: number }[];
  positiveFeedback: number;
  negativeFeedback: number;
  activeUsers: number;
  budget: { monthlyBudgetUsd: number | null; monthSpendUsd: number; usedPercent: number | null; pricesConfigured: boolean; level: 'ok' | 'warning' | 'exceeded' };
  settings: AiUsageSettings;
}

const ACTION_LABELS: Record<string, string> = {
  Pending: 'Pendientes', Executing: 'Procesando', Confirmed: 'Confirmadas', Cancelled: 'Canceladas', Expired: 'Vencidas', Failed: 'Fallidas', Undone: 'Deshechas'
};

/** Tablero de uso del asistente: consultas, tokens, costo estimado, herramientas, errores y tope de gasto mensual. */
@Component({
  selector: 'app-ai-usage-page',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, MatButtonModule, MatCardModule, MatIconModule, RouterLink],
  template: `
    <section class="usage-page">
      <mat-card class="card header">
        <div>
          <p class="eyebrow">Administración</p>
          <h2>Uso de IA</h2>
          <p class="muted">Consultas, tokens y costo estimado de Oficina Inteligente. El costo se calcula con los precios por millón de tokens que cargues abajo.</p>
        </div>
        <div class="header-actions">
          <select name="days" [(ngModel)]="days" (ngModelChange)="load()" aria-label="Período">
            <option [ngValue]="7">Últimos 7 días</option><option [ngValue]="30">Últimos 30 días</option><option [ngValue]="90">Últimos 90 días</option>
          </select>
          <a mat-stroked-button routerLink="/administracion/configuracion-ia"><mat-icon>tune</mat-icon> Proveedor</a>
        </div>
      </mat-card>

      @if (error) { <p class="banner error" role="alert">{{ error }}</p> }
      @if (report; as r) {
        <div class="stats">
          <div class="stat"><span class="value">{{ r.totals.runs }}</span><span class="label">consultas</span></div>
          <div class="stat"><span class="value">USD {{ r.totals.costUsd | number:'1.2-2' }}</span><span class="label">costo estimado</span></div>
          <div class="stat"><span class="value">{{ (r.totals.inputTokens + r.totals.outputTokens) | number }}</span><span class="label">tokens ({{ r.totals.inputTokens | number }} entrada · {{ r.totals.outputTokens | number }} salida)</span></div>
          <div class="stat" [class.bad]="r.totals.failedRuns > 0"><span class="value">{{ r.totals.failedRuns }}</span><span class="label">con error · {{ r.totals.partialRuns }} parciales</span></div>
          <div class="stat"><span class="value">{{ r.activeUsers }}</span><span class="label">usuarios activos</span></div>
          <div class="stat"><span class="value">👍 {{ r.positiveFeedback }} · 👎 {{ r.negativeFeedback }}</span><span class="label">valoraciones</span></div>
        </div>

        <mat-card class="card">
          <h3>Tope mensual</h3>
          @if (!r.budget.pricesConfigured) {
            <p class="muted">Cargá los precios por millón de tokens para estimar el costo y poder aplicar un tope.</p>
          } @else if (r.budget.monthlyBudgetUsd) {
            <div class="meter" [attr.data-level]="r.budget.level" role="meter" [attr.aria-valuenow]="r.budget.usedPercent" aria-valuemin="0" aria-valuemax="100">
              <span [style.width.%]="min(r.budget.usedPercent ?? 0, 100)"></span>
            </div>
            <p class="muted">USD {{ r.budget.monthSpendUsd | number:'1.2-2' }} de USD {{ r.budget.monthlyBudgetUsd | number:'1.2-2' }} este mes ({{ r.budget.usedPercent ?? 0 | number:'1.0-1' }}%).
              @if (r.budget.level === 'warning') { Ya se superó el 80%. } @if (r.budget.level === 'exceeded') { El asistente está pausado hasta el mes siguiente o hasta ampliar el tope. }</p>
          } @else {
            <p class="muted">Gasto estimado del mes: USD {{ r.budget.monthSpendUsd | number:'1.2-2' }}. Sin tope configurado.</p>
          }
        </mat-card>

        <mat-card class="card">
          <h3>Consultas por día</h3>
          <div class="bars" role="img" [attr.aria-label]="'Consultas por día del ' + r.from + ' al ' + r.to">
            @for (day of r.days; track day.date) {
              <div class="bar" [title]="(day.date | date:'dd/MM') + ': ' + day.runs + ' consultas · USD ' + (day.costUsd | number:'1.2-4')">
                <span class="fill" [style.height.%]="maxRuns ? day.runs / maxRuns * 100 : 0" [class.has-errors]="day.failedRuns > 0"></span>
              </div>
            }
          </div>
          <div class="bar-axis"><span>{{ r.from | date:'dd/MM' }}</span><span>{{ r.to | date:'dd/MM' }}</span></div>
        </mat-card>

        <div class="grid">
          <mat-card class="card">
            <h3>Por modelo</h3>
            @for (item of r.byModel; track item.provider + item.model) {
              <div class="row"><span>{{ item.provider }} · {{ item.model || 'sin modelo' }}</span><strong>{{ item.runs }} · USD {{ item.costUsd | number:'1.2-2' }}</strong></div>
            } @empty { <p class="muted">Sin consultas en el período.</p> }
          </mat-card>
          <mat-card class="card">
            <h3>Herramientas más usadas</h3>
            @for (item of r.topTools; track item.name) { <div class="row"><span>{{ item.name }}</span><strong>{{ item.count }}</strong></div> }
            @empty { <p class="muted">Sin datos.</p> }
          </mat-card>
          <mat-card class="card">
            <h3>Errores y rechazos</h3>
            @for (item of r.errors; track item.name) { <div class="row"><span>{{ item.name }}</span><strong>{{ item.count }}</strong></div> }
            @empty { <p class="muted">Sin errores en el período. 🎉</p> }
          </mat-card>
          <mat-card class="card">
            <h3>Acciones propuestas</h3>
            @for (item of r.actions; track item.name) { <div class="row"><span>{{ actionLabel(item.name) }}</span><strong>{{ item.count }}</strong></div> }
            @empty { <p class="muted">Todavía no se propusieron acciones.</p> }
          </mat-card>
        </div>

        <mat-card class="card">
          <h3>Precios y tope</h3>
          <p class="muted">Copiá los precios por millón de tokens (USD) de la lista de precios de tu proveedor para el modelo que usás. El tope pausa el asistente al llegar al 100% y avisa al 80%.</p>
          <form class="settings" (ngSubmit)="saveSettings()">
            <label>Entrada, modelo principal<input type="number" min="0" step="0.01" name="in" [(ngModel)]="settings.inputUsdPerMillion" /></label>
            <label>Salida, modelo principal<input type="number" min="0" step="0.01" name="out" [(ngModel)]="settings.outputUsdPerMillion" /></label>
            <label>Entrada, modelo rápido<input type="number" min="0" step="0.01" name="fin" [(ngModel)]="settings.fastInputUsdPerMillion" placeholder="igual al principal" /></label>
            <label>Salida, modelo rápido<input type="number" min="0" step="0.01" name="fout" [(ngModel)]="settings.fastOutputUsdPerMillion" placeholder="igual al principal" /></label>
            <label>Tope mensual (USD)<input type="number" min="0" step="1" name="budget" [(ngModel)]="settings.monthlyBudgetUsd" placeholder="sin tope" /></label>
            <div class="settings-actions">
              <button mat-flat-button color="primary" type="submit" [disabled]="saving">Guardar</button>
              @if (saved) { <span class="ok">Guardado</span> }
            </div>
          </form>
        </mat-card>
      } @else if (loading) { <p class="muted">Cargando…</p> }
    </section>
  `,
  styles: [`
    .usage-page { display: grid; gap: 12px; }
    .card { padding: 16px 18px; border: 1px solid var(--kd-border-subtle); border-radius: 16px; background: linear-gradient(180deg, rgba(17,26,36,.94), rgba(13,21,30,.98)); color: var(--text-100); }
    .header { display: flex; flex-direction: row; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
    .header-actions { display: flex; gap: 8px; align-items: center; }
    .eyebrow { margin: 0; color: var(--brand-300); font-size: 11px; letter-spacing: 1.2px; text-transform: uppercase; }
    h2 { margin: 2px 0 0; font-size: 22px; }
    h3 { margin: 0 0 10px; font-size: 14px; }
    .muted { margin: 0; color: var(--text-300); font-size: 13px; }
    select, input { padding: 7px 9px; border-radius: 8px; border: 1px solid var(--kd-border-subtle); background: rgba(0,0,0,.2); color: var(--text-100); font: inherit; font-size: 13px; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 10px; }
    .stat { display: grid; gap: 2px; padding: 12px 14px; border-radius: 12px; border: 1px solid var(--kd-border-subtle); background: rgba(255,255,255,.03); }
    .stat.bad { border-color: rgba(255, 99, 99, .35); }
    .value { font-size: 18px; font-weight: 700; }
    .label { color: var(--text-300); font-size: 12px; }
    .meter { height: 10px; margin-bottom: 8px; border-radius: 999px; background: rgba(255,255,255,.06); overflow: hidden; }
    .meter span { display: block; height: 100%; background: var(--brand-300); }
    .meter[data-level="warning"] span { background: #f59e0b; }
    .meter[data-level="exceeded"] span { background: #ef4444; }
    .bars { display: flex; align-items: flex-end; gap: 2px; height: 120px; }
    .bar { display: flex; align-items: flex-end; flex: 1; height: 100%; }
    .fill { display: block; width: 100%; min-height: 1px; border-radius: 3px 3px 0 0; background: rgba(34, 197, 94, .7); }
    .fill.has-errors { background: rgba(245, 158, 11, .8); }
    .bar-axis { display: flex; justify-content: space-between; margin-top: 4px; color: var(--text-300); font-size: 11px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 12px; }
    .row { display: flex; justify-content: space-between; gap: 10px; padding: 5px 0; border-bottom: 1px solid var(--kd-border-subtle); font-size: 13px; }
    .row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .settings { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 10px; margin-top: 10px; }
    .settings label { display: grid; gap: 4px; color: var(--text-300); font-size: 12px; }
    .settings-actions { display: flex; align-items: flex-end; gap: 10px; }
    .ok { color: var(--brand-300); font-size: 12px; }
    .banner.error { margin: 0; padding: 10px 12px; border-radius: 10px; color: #ffb4b4; background: rgba(255, 99, 99, .08); border: 1px solid rgba(255, 99, 99, .25); }
  `]
})
export class AiUsagePageComponent implements OnInit {
  private readonly http = inject(HttpClient);

  days = 30;
  report: AiUsageReport | null = null;
  settings: AiUsageSettings = { inputUsdPerMillion: null, outputUsdPerMillion: null, fastInputUsdPerMillion: null, fastOutputUsdPerMillion: null, monthlyBudgetUsd: null };
  loading = true;
  saving = false;
  saved = false;
  error = '';

  get maxRuns(): number { return Math.max(0, ...(this.report?.days.map(x => x.runs) ?? [0])); }

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.error = '';
    this.http.get<ApiResponse<AiUsageReport>>(`/api/ai/usage?days=${this.days}`, { withCredentials: true })
      .pipe(map(response => response.data), finalize(() => this.loading = false))
      .subscribe({
        next: report => { this.report = report; this.settings = { ...report.settings }; },
        error: () => this.error = 'No se pudo cargar el uso de IA.'
      });
  }

  saveSettings(): void {
    this.saving = true;
    this.saved = false;
    this.error = '';
    const number = (value: number | string | null) => value === null || value === '' ? null : Number(value);
    const payload: AiUsageSettings = {
      inputUsdPerMillion: number(this.settings.inputUsdPerMillion), outputUsdPerMillion: number(this.settings.outputUsdPerMillion),
      fastInputUsdPerMillion: number(this.settings.fastInputUsdPerMillion), fastOutputUsdPerMillion: number(this.settings.fastOutputUsdPerMillion),
      monthlyBudgetUsd: number(this.settings.monthlyBudgetUsd)
    };
    this.http.put<ApiResponse<AiUsageSettings>>('/api/ai/usage/settings', payload, { withCredentials: true })
      .pipe(finalize(() => this.saving = false))
      .subscribe({
        next: () => { this.saved = true; this.load(); },
        error: err => this.error = err?.error?.message ?? 'No se pudieron guardar los precios.'
      });
  }

  actionLabel(status: string): string { return ACTION_LABELS[status] ?? status; }

  min(a: number, b: number): number { return Math.min(a, b); }
}
