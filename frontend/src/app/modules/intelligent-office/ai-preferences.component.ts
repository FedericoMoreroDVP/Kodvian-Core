import { Component, EventEmitter, OnInit, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { finalize } from 'rxjs';

import { GoogleCalendarConnectComponent } from '../../shared/google-calendar/google-calendar-connect.component';
import { AiUserPreferences } from './intelligent-office.models';
import { IntelligentOfficeService } from './intelligent-office.service';

/** Preferencias personales del asistente. También se pueden pedir por chat ("recordá que mis reuniones duran 30 minutos"). */
@Component({
  selector: 'app-ai-preferences',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatIconModule, GoogleCalendarConnectComponent],
  template: `
    <form class="preferences" (ngSubmit)="save()" aria-label="Preferencias del asistente">
      <header>
        <h4><mat-icon>tune</mat-icon> Preferencias del asistente</h4>
        <button mat-icon-button type="button" aria-label="Cerrar preferencias" (click)="closed.emit()"><mat-icon>close</mat-icon></button>
      </header>
      <p class="hint">Se aplican a tus próximas respuestas. También podés pedírselas por chat, por ejemplo: "recordá que mis reuniones duran 30 minutos".</p>
      @if (loading) { <p class="hint">Cargando…</p> } @else {
        <div class="grid">
          <label>Moneda que ves primero
            <select name="currency" [(ngModel)]="model.preferredCurrency">
              <option [ngValue]="null">Sin preferencia</option><option value="ARS">ARS</option><option value="USD">USD</option>
            </select>
          </label>
          <label>Duración de reuniones (min)
            <input name="minutes" type="number" min="15" max="480" step="5" [(ngModel)]="model.defaultMeetingMinutes" placeholder="60" />
          </label>
          <label>Prioridad de tareas
            <select name="priority" [(ngModel)]="model.defaultTaskPriority">
              <option [ngValue]="null">Sin preferencia</option><option>Baja</option><option>Media</option><option>Alta</option><option>Urgente</option>
            </select>
          </label>
          <label>Estilo de respuesta
            <select name="style" [(ngModel)]="model.responseStyle">
              <option [ngValue]="null">Normal</option><option value="breve">Breve</option><option value="detallado">Detallado</option>
            </select>
          </label>
          <label>"Mi día" muestra
            <select name="scope" [(ngModel)]="model.defaultScope">
              <option [ngValue]="null">Según mi rol</option><option value="todo">Toda la empresa</option><option value="mio">Solo lo mío</option>
            </select>
          </label>
        </div>
        <label class="notes">Indicaciones para el asistente
          <textarea name="notes" rows="2" maxlength="500" [(ngModel)]="model.notes" placeholder="Ej.: cuando digo 'el portal' me refiero al proyecto Portal Acme"></textarea>
        </label>
        @if (error) { <p class="error" role="alert">{{ error }}</p> }
        @if (saved) { <p class="ok" role="status"><mat-icon>check_circle</mat-icon> Guardado</p> }
        <app-google-calendar-connect />
        <div class="buttons">
          <button mat-flat-button color="primary" type="submit" [disabled]="saving">Guardar</button>
          <button mat-button type="button" (click)="closed.emit()">Cerrar</button>
        </div>
      }
    </form>
  `,
  styles: [`
    .preferences { display: grid; gap: 10px; margin: 12px 18px 0; padding: 14px; border-radius: 14px; border: 1px solid var(--kd-border-subtle); background: rgba(255,255,255,.03); }
    header { display: flex; align-items: center; justify-content: space-between; }
    h4 { display: flex; align-items: center; gap: 6px; margin: 0; font-size: 14px; color: var(--text-100); }
    h4 mat-icon { color: var(--brand-300); }
    .hint { margin: 0; color: var(--text-300); font-size: 12px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 10px; }
    label { display: grid; gap: 4px; color: var(--text-300); font-size: 12px; }
    select, input, textarea { padding: 7px 9px; border-radius: 8px; border: 1px solid var(--kd-border-subtle); background: rgba(0,0,0,.2); color: var(--text-100); font: inherit; font-size: 13px; }
    textarea { resize: vertical; }
    .buttons { display: flex; gap: 8px; }
    .error { margin: 0; color: #ffb4b4; font-size: 12px; }
    .ok { display: flex; align-items: center; gap: 4px; margin: 0; color: var(--brand-300); font-size: 12px; }
    .ok mat-icon { width: 16px; height: 16px; font-size: 16px; }
  `]
})
export class AiPreferencesComponent implements OnInit {
  private readonly api = inject(IntelligentOfficeService);
  @Output() readonly closed = new EventEmitter<void>();

  model: AiUserPreferences = { preferredCurrency: null, defaultMeetingMinutes: null, defaultTaskPriority: null, responseStyle: null, defaultScope: null, notes: null };
  loading = true;
  saving = false;
  saved = false;
  error = '';

  ngOnInit(): void {
    this.api.getPreferences().pipe(finalize(() => this.loading = false)).subscribe({
      next: preferences => this.model = { ...preferences },
      error: () => this.error = 'No se pudieron cargar tus preferencias.'
    });
  }

  save(): void {
    this.saving = true;
    this.saved = false;
    this.error = '';
    const minutes = this.model.defaultMeetingMinutes;
    const payload: AiUserPreferences = { ...this.model, defaultMeetingMinutes: minutes === null || minutes === undefined || `${minutes}` === '' ? null : Number(minutes) };
    this.api.savePreferences(payload).pipe(finalize(() => this.saving = false)).subscribe({
      next: preferences => { this.model = { ...preferences }; this.saved = true; },
      error: error => this.error = error?.error?.message ?? 'No se pudieron guardar las preferencias.'
    });
  }
}
