import { Component, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { AI_PROVIDERS, AiProviderConfiguration, AiProviderId, AiProviderOption, providerOption } from './ai-provider-configuration.models';
import { AiProviderConfigurationService } from './ai-provider-configuration.service';

@Component({
  selector: 'app-ai-provider-configuration-page',
  standalone: true,
  imports: [DatePipe, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, MatSelectModule, RouterLink],
  template: `
    <section class="configuration-page">
      <mat-card class="hero-card">
        <p class="eyebrow">Administración · Oficina Inteligente</p>
        <h2>Proveedor de IA</h2>
        <p>Configura la conexión que usará esta instalación. La clave se guarda cifrada y no se vuelve a mostrar.</p>
      </mat-card>

      <mat-card class="panel-card">
        @if (loading) { <p role="status">Cargando configuración…</p> }
        @if (error) { <p class="error" role="alert">{{ error }}</p> }
        @if (message) { <p class="message" role="status">{{ message }}</p> }
        @if (configuration) {
          <div class="status-row">
            <strong>Estado:</strong>
            <span>{{ configuration.isConnectionVerified ? 'Conexión verificada' : configuration.isConfigured ? 'Requiere verificación' : 'Sin configurar' }}</span>
            @if (configuration.apiKeyHint) { <span class="key-hint">Clave {{ configuration.apiKeyHint }}</span> }
          </div>
          <form (ngSubmit)="save()" class="settings-form">
            <mat-form-field appearance="outline">
              <mat-label>Proveedor</mat-label>
              <mat-select name="provider" [(ngModel)]="provider" (selectionChange)="onProviderChange()">
                @for (option of providers; track option.id) { <mat-option [value]="option.id">{{ option.label }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Identificador del modelo</mat-label>
              <input matInput name="model" [(ngModel)]="model" required maxlength="100" autocomplete="off" [placeholder]="selectedProvider.modelPlaceholder" />
              <mat-hint>{{ selectedProvider.modelHint }}</mat-hint>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ keyRequired ? 'Clave API' : 'Nueva clave API (opcional)' }}</mat-label>
              <input matInput name="apiKey" [(ngModel)]="apiKey" [required]="keyRequired" type="password" maxlength="512" autocomplete="new-password" />
              <mat-hint>{{ keyRequired && providerChanged ? 'Cambiaste de proveedor: ingresá la clave del nuevo proveedor.' : keyRequired ? selectedProvider.keyHint : 'Déjala vacía para conservar la clave guardada.' }}</mat-hint>
            </mat-form-field>
            <div class="actions">
              <button mat-flat-button type="submit" [disabled]="busy || !model.trim() || (keyRequired && !apiKey.trim())">Guardar configuración</button>
              <button mat-stroked-button type="button" (click)="testConnection()" [disabled]="busy || !configuration.isConfigured || hasUnsavedChanges">Probar conexión</button>
              @if (configuration.isConfigured) { <button mat-button type="button" (click)="remove()" [disabled]="busy">Quitar proveedor</button> }
            </div>
          </form>
          @if (configuration.isConfigured && hasUnsavedChanges) { <p class="warning" role="status">Hay cambios sin guardar. Guardá la configuración antes de probar la conexión.</p> }
          @if (looksLikeWorkspaceId) { <p class="warning" role="alert">Eso parece el ID de un workspace, no de un modelo. Escribí el nombre del modelo, por ejemplo {{ selectedProvider.defaultModel || 'el identificador del modelo' }}.</p> }
          <p class="notice">{{ selectedProvider.testNotice }}</p>
          @if (configuration.isConfigured) {
            <section class="privacy-approval" aria-labelledby="privacy-title">
              <h3 id="privacy-title">Uso de datos reales</h3>
              <p>{{ savedProvider.privacyNotice }}</p>
              @if (configuration.liveDataApproved) {
                <p class="message">Aprobado el {{ configuration.liveDataApprovedAtUtc | date:'short' }}. Si cambias la clave o el modelo, tendrás que aprobarlo de nuevo.</p>
                <button mat-button type="button" (click)="revokeApproval()" [disabled]="busy">Revocar aprobación</button>
              } @else {
                <label class="approval-label"><input type="checkbox" name="privacyAccepted" [(ngModel)]="privacyAccepted" /> Confirmo que revisé y aprobé las condiciones de privacidad y retención de {{ savedProvider.company }} y configuré un límite de gasto para la cuenta de API de esta instalación.</label>
                <button mat-flat-button type="button" (click)="approveLiveData()" [disabled]="busy || !configuration.isConnectionVerified || !privacyAccepted">Aprobar uso de datos reales</button>
              }
            </section>
          }
        }
        @if (busy) { <mat-spinner diameter="28" aria-label="Procesando" /> }
        <a mat-button routerLink="/administracion">Volver a Administración</a>
      </mat-card>
    </section>
  `,
  styles: [`
    .configuration-page { display: grid; gap: 12px; max-width: 900px; }
    .hero-card, .panel-card { padding: 18px; border-radius: 16px; border: 1px solid var(--kd-border-subtle); background: linear-gradient(180deg, rgba(17,26,36,.94), rgba(13,21,30,.98)); color: var(--text-100); }
    .eyebrow { margin: 0 0 6px; color: var(--brand-300); font-size: 11px; letter-spacing: 1.2px; text-transform: uppercase; }
    h2 { margin: 0; color: var(--brand-300); font-size: 30px; }
    .settings-form { display: grid; gap: 10px; max-width: 640px; margin-top: 18px; }
    .status-row, .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
    .status-row { padding: 12px 0; }
    .key-hint, .notice { color: var(--text-300); }
    .notice { margin-top: 18px; font-size: 13px; }
    .privacy-approval { margin: 18px 0; padding: 16px; border: 1px solid var(--kd-border-subtle); border-radius: 12px; }
    .privacy-approval h3 { margin-top: 0; color: var(--kd-text-secondary); }
    .approval-label { display: flex; align-items: flex-start; gap: 10px; margin: 14px 0; line-height: 1.5; }
    .error { color: #ff9b9b; }
    .warning { margin: 12px 0 0; color: #ffd08a; font-size: 13px; }
    .message { color: var(--brand-300); }
    mat-spinner { margin: 12px 0; }
  `]
})
export class AiProviderConfigurationPageComponent implements OnInit {
  private readonly api = inject(AiProviderConfigurationService);
  readonly providers = AI_PROVIDERS;
  configuration?: AiProviderConfiguration;
  provider: AiProviderId = 'OpenAI';
  model = '';
  apiKey = '';
  privacyAccepted = false;
  loading = true;
  busy = false;
  error = '';
  message = '';

  ngOnInit(): void { this.load(); }

  get selectedProvider(): AiProviderOption { return providerOption(this.provider); }

  get savedProvider(): AiProviderOption { return providerOption(this.configuration?.provider); }

  get providerChanged(): boolean { return !!this.configuration?.isConfigured && this.configuration.provider !== this.provider; }

  get hasUnsavedChanges(): boolean {
    if (!this.configuration?.isConfigured) return false;
    return this.providerChanged || this.model.trim() !== (this.configuration.model ?? '') || this.apiKey.trim() !== '';
  }

  get looksLikeWorkspaceId(): boolean { return /^wrkspc_/i.test(this.model.trim()); }

  get keyRequired(): boolean { return !this.configuration?.isConfigured || this.providerChanged; }

  onProviderChange(): void {
    const previousDefaults = this.providers.map(x => x.defaultModel).filter(Boolean);
    if (!this.model.trim() || previousDefaults.includes(this.model.trim()))
      this.model = this.selectedProvider.defaultModel;
    this.apiKey = '';
  }

  load(): void {
    this.loading = true;
    this.error = '';
    this.api.get().pipe(finalize(() => this.loading = false)).subscribe({
      next: value => {
        this.configuration = value;
        this.provider = providerOption(value.provider).id;
        this.model = value.model ?? (value.isConfigured ? '' : this.selectedProvider.defaultModel);
      },
      error: () => this.error = 'No se pudo cargar la configuración.'
    });
  }

  save(): void {
    if (!this.configuration || this.busy) return;
    this.busy = true;
    this.error = '';
    this.message = '';
    this.api.save({ provider: this.provider, model: this.model.trim(), apiKey: this.apiKey, version: this.configuration.version })
      .pipe(finalize(() => this.busy = false))
      .subscribe({
        next: value => {
          this.configuration = value;
          this.apiKey = '';
          this.message = 'Configuración guardada. Prueba la conexión antes de habilitar el agente.';
        },
        error: err => this.error = err?.error?.message || 'No se pudo guardar. Revisa el modelo y la clave API.'
      });
  }

  testConnection(): void {
    if (!this.configuration?.isConfigured || this.busy) return;
    this.busy = true;
    this.error = '';
    this.message = '';
    this.api.test().pipe(finalize(() => this.busy = false)).subscribe({
      next: result => {
        this.configuration = { ...this.configuration!, isConnectionVerified: result.isSuccessful, lastConnectionTestAtUtc: result.testedAtUtc };
        if (!result.isSuccessful) this.privacyAccepted = false;
        this.message = result.message;
      },
      error: () => this.error = 'No se pudo completar la prueba de conexión.'
    });
  }

  approveLiveData(): void {
    if (!this.configuration?.isConnectionVerified || !this.privacyAccepted || this.busy) return;
    this.busy = true;
    this.error = '';
    this.api.approveLiveData(this.configuration.requiredLiveDataApprovalTermsVersion).pipe(finalize(() => this.busy = false)).subscribe({
      next: value => { this.configuration = value; this.privacyAccepted = false; this.message = 'Se registró la aprobación para enviar datos autorizados al proveedor.'; },
      error: () => this.error = 'No se pudo registrar la aprobación.'
    });
  }

  revokeApproval(): void {
    if (!this.configuration?.liveDataApproved || this.busy) return;
    this.busy = true;
    this.error = '';
    this.api.revokeLiveDataApproval().pipe(finalize(() => this.busy = false)).subscribe({
      next: value => { this.configuration = value; this.privacyAccepted = false; this.message = 'Se revocó la aprobación para enviar datos reales.'; },
      error: () => this.error = 'No se pudo revocar la aprobación.'
    });
  }

  remove(): void {
    if (!this.configuration?.isConfigured || this.busy) return;
    if (!globalThis.confirm('¿Quitar la clave y la configuración del proveedor de IA?')) return;
    this.busy = true;
    this.error = '';
    this.api.remove(this.configuration.version).pipe(finalize(() => this.busy = false)).subscribe({
      next: () => { this.apiKey = ''; this.model = ''; this.message = 'Se quitó la configuración del proveedor.'; this.load(); },
      error: () => this.error = 'No se pudo quitar la configuración.'
    });
  }
}
