import { Component, Input, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';

import { GoogleCalendarService, GoogleCalendarStatus } from './google-calendar.service';

const CALLBACK_MESSAGES: Record<string, string> = {
  connected: 'Listo: Google Calendar quedó conectado. Tus próximas reuniones se van a agendar ahí.',
  denied: 'No se conectó Google Calendar porque se canceló el permiso en Google.',
  invalid_state: 'La conexión venció o no es válida. Probá conectar de nuevo.',
  exchange_failed: 'Google no confirmó la conexión. Probá de nuevo en unos minutos.',
  disabled: 'La integración con Google Calendar todavía no está configurada en el servidor.'
};

/**
 * Botón "Conectar Google Calendar" con el estado de la conexión del usuario.
 * compact = true muestra solo un botón chico (encabezado de Agenda).
 */
@Component({
  selector: 'app-google-calendar-connect',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    @if (status; as s) {
      @if (compact) {
        @if (s.connected) {
          <button mat-stroked-button type="button" class="chip connected" [matTooltip]="'Conectado con ' + s.email + ' · tocá para desconectar'" (click)="disconnect()" [disabled]="busy">
            <mat-icon>event_available</mat-icon><span class="label">Google Calendar</span>
          </button>
        } @else if (s.enabled) {
          <button mat-stroked-button type="button" class="chip" matTooltip="Agendá tus reuniones de Kodvian en tu Google Calendar" (click)="connect()" [disabled]="busy">
            <mat-icon>calendar_add_on</mat-icon><span class="label">Conectar Google Calendar</span>
          </button>
        }
      } @else {
        <section class="card" aria-label="Google Calendar">
          <div class="head"><mat-icon>calendar_month</mat-icon><strong>Google Calendar</strong></div>
          @if (!s.enabled) {
            <p class="muted">Todavía no está configurado en el servidor. Un Administrador tiene que cargar las credenciales de Google.</p>
          } @else if (s.connected) {
            <p>Conectado con <strong>{{ s.email }}</strong>. Las reuniones que organices se agendan en ese calendario, con los participantes como invitados.</p>
            @if (s.lastError) { <p class="error">{{ s.lastError }}</p> }
            <div class="buttons">
              @if (s.lastError) { <button mat-flat-button color="primary" type="button" (click)="connect()" [disabled]="busy">Volver a conectar</button> }
              <button mat-stroked-button type="button" (click)="disconnect()" [disabled]="busy">Desconectar</button>
            </div>
          } @else {
            <p class="muted">Conectá tu cuenta de Google para que las reuniones que organices en Kodvian aparezcan en tu Google Calendar.</p>
            <div class="buttons"><button mat-flat-button color="primary" type="button" (click)="connect()" [disabled]="busy"><mat-icon>calendar_add_on</mat-icon> Conectar Google Calendar</button></div>
          }
        </section>
      }
    }
    @if (message) { <p class="message" [class.error]="messageIsError" role="status">{{ message }}</p> }
  `,
  styles: [`
    :host { display: block; }
    .chip { border-radius: 999px; }
    .chip.connected { color: var(--brand-300); border-color: rgba(34, 197, 94, .45); }
    .card { display: grid; gap: 8px; padding: 12px 14px; border-radius: 12px; border: 1px solid var(--kd-border-subtle); background: rgba(255,255,255,.03); font-size: 13px; color: var(--text-100); }
    .head { display: flex; align-items: center; gap: 6px; }
    .head mat-icon { color: var(--brand-300); }
    p { margin: 0; }
    .muted { color: var(--text-300); }
    .error { color: #ffb4b4; }
    .buttons { display: flex; flex-wrap: wrap; gap: 8px; }
    .message { margin: 6px 0 0; font-size: 12px; color: var(--brand-300); }
    @media (max-width: 600px) { .chip .label { display: none; } }
  `]
})
export class GoogleCalendarConnectComponent implements OnInit {
  private readonly api = inject(GoogleCalendarService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router);
  @Input() compact = false;
  /** Muestra el resultado de volver de Google (?googleCalendar=connected|error). */
  @Input() handleCallback = false;

  status: GoogleCalendarStatus | null = null;
  busy = false;
  message = '';
  messageIsError = false;

  ngOnInit(): void {
    if (this.handleCallback) this.readCallbackResult();
    this.load();
  }

  load(): void {
    this.api.status().subscribe({ next: status => this.status = status, error: () => this.status = null });
  }

  connect(): void {
    this.busy = true;
    this.api.connect().pipe(finalize(() => this.busy = false)).subscribe({
      next: url => globalThis.location.assign(url),
      error: error => this.show(error?.error?.message ?? 'No se pudo iniciar la conexión con Google.', true)
    });
  }

  disconnect(): void {
    if (!globalThis.confirm('¿Desconectar Google Calendar? Las reuniones nuevas ya no se van a agendar en tu calendario.')) return;
    this.busy = true;
    this.api.disconnect().pipe(finalize(() => this.busy = false)).subscribe({
      next: () => { this.show('Google Calendar desconectado.', false); this.load(); },
      error: () => this.show('No se pudo desconectar Google Calendar.', true)
    });
  }

  private readCallbackResult(): void {
    const params = this.route?.snapshot.queryParamMap;
    const result = params?.get('googleCalendar');
    if (!result) return;
    const key = result === 'connected' ? 'connected' : params?.get('reason') ?? 'exchange_failed';
    this.show(CALLBACK_MESSAGES[key] ?? CALLBACK_MESSAGES['exchange_failed'], result !== 'connected');
    void this.router.navigate([], { relativeTo: this.route, queryParams: { googleCalendar: null, reason: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  private show(message: string, isError: boolean): void {
    this.message = message;
    this.messageIsError = isError;
  }
}
