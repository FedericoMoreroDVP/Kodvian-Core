import { DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';

import { AiPendingAction } from './intelligent-office.models';

const ACTION_ICONS: Record<string, string> = {
  'tasks.create': 'add_task',
  'tasks.update_status': 'published_with_changes',
  'tasks.update': 'edit_calendar',
  'meetings.create': 'event',
  'meetings.update': 'update',
  'meetings.cancel': 'event_busy',
  'finance.create_movement': 'payments',
  'clients.create_prospect': 'person_add'
};

const ACTION_STATUS_LABELS: Record<string, string> = {
  Pending: 'Pendiente de confirmación',
  Executing: 'Procesando…',
  Confirmed: 'Confirmada',
  Cancelled: 'Cancelada',
  Expired: 'Vencida',
  Failed: 'No se pudo completar',
  Undone: 'Deshecha'
};

/** Tarjeta de una acción propuesta por el asistente: resumen, detalles y Confirmar / Cancelar / Deshacer. */
@Component({
  selector: 'app-ai-action-card',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule, RouterLink],
  template: `
    <section class="action-card" [attr.data-status]="action.status" [attr.aria-label]="action.title">
      <header class="action-header">
        <mat-icon class="action-icon">{{ icon }}</mat-icon>
        <span class="action-title">{{ action.title }}</span>
        <span class="action-status">{{ statusLabel }}</span>
      </header>
      <p class="action-summary">{{ action.summary }}</p>
      @if (action.details.length) {
        <dl class="action-details">
          @for (detail of action.details; track $index) {
            <div class="action-detail"><dt>{{ detail.label }}</dt><dd>{{ detail.value }}</dd></div>
          }
        </dl>
      }
      @if (actionable) {
        <div class="action-buttons">
          <button mat-flat-button color="primary" type="button" class="confirm-action" (click)="confirm.emit(action)" [disabled]="disabled">
            @if (inProgress) { <mat-spinner diameter="16" /> } @else { <mat-icon>check</mat-icon> }
            Confirmar
          </button>
          <button mat-stroked-button type="button" class="cancel-action" (click)="cancel.emit(action)" [disabled]="disabled">
            <mat-icon>close</mat-icon> Cancelar
          </button>
          <span class="action-expiry">Vence a las {{ action.expiresAtUtc | date:'HH:mm' }}</span>
        </div>
      } @else if (action.resultMessage) {
        <p class="action-result">
          {{ action.resultMessage }}
          @if (action.resultLink && action.status === 'Confirmed') { <a [routerLink]="action.resultLink">Ver</a> }
        </p>
      }
      @if (undoable) {
        <div class="action-buttons">
          <button mat-stroked-button type="button" class="undo-action" (click)="undo.emit(action)" [disabled]="disabled">
            @if (inProgress) { <mat-spinner diameter="16" /> } @else { <mat-icon>undo</mat-icon> }
            Deshacer
          </button>
          <span class="action-expiry">Disponible hasta las {{ action.undoAvailableUntilUtc | date:'HH:mm' }}</span>
        </div>
      }
    </section>
  `,
  styles: [`
    .action-card { margin-top: 10px; padding: 12px; border-radius: 12px; border: 1px solid rgba(34, 197, 94, .35); background: rgba(34, 197, 94, .05); }
    .action-card[data-status="Confirmed"] { border-color: rgba(34, 197, 94, .25); background: rgba(34, 197, 94, .03); }
    .action-card[data-status="Cancelled"], .action-card[data-status="Expired"], .action-card[data-status="Undone"] { border-color: var(--kd-border-subtle); background: rgba(255,255,255,.02); opacity: .8; }
    .action-card[data-status="Failed"] { border-color: rgba(255, 99, 99, .3); background: rgba(255, 99, 99, .05); }
    .action-header { display: flex; align-items: center; gap: 8px; }
    .action-icon { flex-shrink: 0; color: var(--brand-300); }
    .action-title { flex: 1; font-weight: 600; font-size: 14px; }
    .action-status { padding: 2px 8px; border-radius: 999px; font-size: 11px; color: var(--text-300); background: rgba(255,255,255,.05); }
    .action-card[data-status="Confirmed"] .action-status { color: var(--brand-300); }
    .action-card[data-status="Failed"] .action-status { color: #ffb4b4; }
    .action-summary { margin: 8px 0 0; font-size: 13px; line-height: 1.45; }
    .action-details { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 6px 14px; margin: 10px 0 0; }
    .action-detail dt { color: var(--text-300); font-size: 11px; }
    .action-detail dd { margin: 1px 0 0; font-size: 13px; }
    .action-buttons { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 12px; }
    .action-buttons mat-spinner { display: inline-block; margin-right: 6px; }
    .action-expiry { color: var(--text-300); font-size: 11px; }
    .action-result { margin: 10px 0 0; font-size: 13px; color: var(--text-300); }
    .action-result a { margin-left: 6px; color: var(--brand-300); }
  `]
})
export class AiActionCardComponent {
  @Input({ required: true }) action!: AiPendingAction;
  /** Deshabilita los botones (otra consulta o acción en curso). */
  @Input() disabled = false;
  @Input() inProgress = false;
  @Output() readonly confirm = new EventEmitter<AiPendingAction>();
  @Output() readonly cancel = new EventEmitter<AiPendingAction>();
  @Output() readonly undo = new EventEmitter<AiPendingAction>();

  get icon(): string { return ACTION_ICONS[this.action.toolName] ?? 'bolt'; }

  get actionable(): boolean { return isActionable(this.action); }

  get undoable(): boolean {
    return this.action.status === 'Confirmed' && !!this.action.canUndo && !!this.action.undoAvailableUntilUtc
      && new Date(this.action.undoAvailableUntilUtc).getTime() > Date.now();
  }

  get statusLabel(): string {
    const status = this.action.status === 'Pending' && !this.actionable ? 'Expired' : this.action.status;
    return ACTION_STATUS_LABELS[status] ?? status;
  }
}

export function isActionable(action: AiPendingAction): boolean {
  return action.status === 'Pending' && new Date(action.expiresAtUtc).getTime() > Date.now();
}
