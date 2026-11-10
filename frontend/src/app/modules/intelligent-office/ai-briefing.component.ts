import { DecimalPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

import { AiBriefing } from './intelligent-office.models';

const ALERT_ICONS: Record<string, string> = {
  overdue_tasks: 'assignment_late',
  project_risk: 'report',
  expense_spike: 'trending_up',
  overdue_collections: 'request_quote'
};

/** Resumen del día y alertas al abrir Oficina Inteligente; cada alerta propone una consulta al asistente. */
@Component({
  selector: 'app-ai-briefing',
  standalone: true,
  imports: [DecimalPipe, MatButtonModule, MatIconModule, RouterLink],
  template: `
    <section class="briefing" aria-label="Resumen del día">
      @if (briefing.greeting) { <h4 class="greeting">{{ briefing.greeting }} 👋</h4> }
      <div class="stats">
        @if (briefing.tasks; as tasks) {
          <button type="button" class="stat" [class.alert]="tasks.overdue > 0" (click)="ask.emit('¿Qué tareas tengo vencidas o que vencen esta semana?')">
            <mat-icon>task_alt</mat-icon>
            <span class="stat-value">{{ tasks.overdue }}</span><span class="stat-label">vencidas</span>
            <span class="stat-extra">{{ tasks.dueToday }} vencen hoy · {{ tasks.dueThisWeek }} esta semana</span>
          </button>
        }
        @if (briefing.meetings; as meetings) {
          <button type="button" class="stat" (click)="ask.emit('¿Qué reuniones tengo esta semana?')">
            <mat-icon>calendar_month</mat-icon>
            <span class="stat-value">{{ meetings.today }}</span><span class="stat-label">reuniones hoy</span>
            <span class="stat-extra">{{ meetings.thisWeek }} en los próximos 7 días</span>
          </button>
        }
        @for (money of briefing.collections ?? []; track money.currency) {
          <button type="button" class="stat" [class.alert]="money.overdue > 0" (click)="ask.emit('¿Quién me debe en ' + money.currency + '?')">
            <mat-icon>payments</mat-icon>
            <span class="stat-value">{{ money.currency }} {{ money.pending | number:'1.0-0' }}</span><span class="stat-label">a cobrar</span>
            <span class="stat-extra">{{ money.overdue | number:'1.0-0' }} vencido · {{ money.count }} pendientes</span>
          </button>
        }
      </div>

      @if (briefing.alerts.length) {
        <ul class="alerts">
          @for (alert of briefing.alerts; track alert.title) {
            <li class="alert-item" [attr.data-severity]="alert.severity">
              <mat-icon class="alert-icon">{{ icon(alert.kind) }}</mat-icon>
              <div class="alert-body">
                <strong>{{ alert.title }}</strong>
                <span>{{ alert.detail }}</span>
              </div>
              <div class="alert-actions">
                <button mat-stroked-button type="button" class="alert-ask" (click)="ask.emit(alert.prompt)">Resolver con IA</button>
                @if (alert.link) { <a mat-button [routerLink]="alert.link">Abrir</a> }
              </div>
            </li>
          }
        </ul>
      } @else if (hasData) {
        <p class="all-good"><mat-icon>check_circle</mat-icon> No hay alertas: todo al día.</p>
      }

      @if (briefing.tasks?.topOverdue?.length || briefing.meetings?.next?.length) {
        <div class="lists">
          @if (briefing.tasks?.topOverdue?.length) {
            <div class="list">
              <span class="list-title">Tareas más atrasadas</span>
              @for (item of briefing.tasks!.topOverdue; track item.id) { <span class="list-item">{{ item.title }} <small>{{ item.detail }}</small></span> }
            </div>
          }
          @if (briefing.meetings?.next?.length) {
            <div class="list">
              <span class="list-title">Próximas reuniones</span>
              @for (item of briefing.meetings!.next; track item.id) { <span class="list-item">{{ item.title }} <small>{{ item.detail }}</small></span> }
            </div>
          }
        </div>
      }
    </section>
  `,
  styles: [`
    .briefing { display: grid; gap: 12px; width: 100%; max-width: 760px; margin: 0 auto 18px; text-align: left; }
    .greeting { margin: 0; font-size: 18px; color: var(--text-100); }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 8px; }
    .stat { display: grid; grid-template-columns: auto 1fr; align-items: baseline; column-gap: 8px; padding: 10px 12px; border-radius: 12px; text-align: left; font: inherit;
      color: var(--text-100); background: rgba(255,255,255,.03); border: 1px solid var(--kd-border-subtle); cursor: pointer; }
    .stat:hover { border-color: rgba(34, 197, 94, .45); }
    .stat.alert { border-color: rgba(255, 180, 80, .45); }
    .stat mat-icon { grid-row: span 3; align-self: center; color: var(--brand-300); }
    .stat-value { font-size: 18px; font-weight: 700; }
    .stat-label, .stat-extra { grid-column: 2; color: var(--text-300); font-size: 12px; }
    .alerts { display: grid; gap: 6px; margin: 0; padding: 0; list-style: none; }
    .alert-item { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: 12px; border: 1px solid rgba(255, 180, 80, .35); background: rgba(255, 180, 80, .06); }
    .alert-item[data-severity="critical"] { border-color: rgba(255, 99, 99, .4); background: rgba(255, 99, 99, .07); }
    .alert-item[data-severity="info"] { border-color: var(--kd-border-subtle); background: rgba(255,255,255,.03); }
    .alert-icon { flex-shrink: 0; color: #ffc77a; }
    .alert-item[data-severity="critical"] .alert-icon { color: #ffb4b4; }
    .alert-body { display: grid; gap: 2px; flex: 1; min-width: 0; font-size: 13px; }
    .alert-body span { color: var(--text-300); font-size: 12px; }
    .alert-actions { display: flex; flex-wrap: wrap; gap: 4px; flex-shrink: 0; }
    .all-good { display: flex; align-items: center; gap: 6px; margin: 0; color: var(--brand-300); font-size: 13px; }
    .lists { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 10px; }
    .list { display: grid; gap: 4px; font-size: 13px; }
    .list-title { color: var(--text-300); font-size: 11px; text-transform: uppercase; letter-spacing: .8px; }
    .list-item small { display: block; color: var(--text-300); font-size: 11px; }
    @media (max-width: 760px) { .alert-item { flex-wrap: wrap; } }
  `]
})
export class AiBriefingComponent {
  @Input({ required: true }) briefing!: AiBriefing;
  @Output() readonly ask = new EventEmitter<string>();

  get hasData(): boolean { return !!(this.briefing.tasks || this.briefing.meetings || this.briefing.collections?.length); }

  icon(kind: string): string { return ALERT_ICONS[kind] ?? 'notifications'; }
}
