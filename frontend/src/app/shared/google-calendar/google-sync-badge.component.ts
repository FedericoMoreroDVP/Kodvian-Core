import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { finalize } from 'rxjs';

import { GoogleCalendarService, GoogleCalendarSyncResult } from './google-calendar.service';

/** Estado de sincronización de una reunión con Google Calendar, con reintento manual si falló. */
@Component({
  selector: 'app-google-sync-badge',
  standalone: true,
  imports: [MatIconModule, MatTooltipModule],
  template: `
    @if (status === 'Synced') {
      <span class="badge synced"><mat-icon>event_available</mat-icon> En Google Calendar</span>
    } @else if (status === 'Failed') {
      <span class="badge failed" [matTooltip]="error || 'No se pudo sincronizar con Google Calendar.'"><mat-icon>sync_problem</mat-icon> No sincronizada</span>
      <button type="button" class="retry" (click)="retry()" [disabled]="busy">{{ busy ? 'Reintentando…' : 'Reintentar' }}</button>
    }
  `,
  styles: [`
    :host { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 6px; }
    .badge { display: inline-flex; align-items: center; gap: 4px; padding: 2px 8px; border-radius: 999px; font-size: 11px; }
    .badge mat-icon { width: 14px; height: 14px; font-size: 14px; }
    .synced { color: var(--brand-300); background: rgba(34, 197, 94, .1); }
    .failed { color: #ffc77a; background: rgba(255, 180, 80, .1); }
    .retry { padding: 2px 8px; border: 1px solid var(--kd-border-subtle); border-radius: 999px; background: transparent; color: var(--text-100); font: inherit; font-size: 11px; cursor: pointer; }
  `]
})
export class GoogleSyncBadgeComponent {
  private readonly api = inject(GoogleCalendarService);
  @Input({ required: true }) meetingId!: string;
  @Input() status: 'Synced' | 'Failed' | null | undefined;
  @Input() error: string | null | undefined;
  @Output() readonly synced = new EventEmitter<GoogleCalendarSyncResult>();
  busy = false;

  retry(): void {
    this.busy = true;
    this.api.retry(this.meetingId).pipe(finalize(() => this.busy = false)).subscribe({
      next: result => { this.status = result.status; this.error = result.error; this.synced.emit(result); },
      error: err => this.error = err?.error?.message ?? 'No se pudo reintentar.'
    });
  }
}
