import { DatePipe } from '@angular/common';
import { AfterViewChecked, Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { AuthSessionService } from '../../core/auth/auth-session.service';
import { AiActionCardComponent, isActionable } from './ai-action-card.component';
import { AiBriefingComponent } from './ai-briefing.component';
import { AiMessageExtrasComponent } from './ai-message-extras.component';
import { AiPreferencesComponent } from './ai-preferences.component';
import { AiVoiceButtonComponent } from './ai-voice-button.component';
import { renderAiMarkdown } from './ai-markdown';
import { AiBriefing, AiConversation, AiMessage, AiPendingAction, AiSendResult, AiSourceReference } from './intelligent-office.models';
import { IntelligentOfficeService, StreamUnavailableError } from './intelligent-office.service';

const SOURCE_LABELS: Record<string, string> = {
  'dashboard.get_snapshot': 'Panel de indicadores',
  'finance.get_summary': 'Finanzas',
  'projects.list': 'Proyectos',
  'tasks.list': 'Tareas',
  'tasks.create': 'Tareas',
  'tasks.update_status': 'Tareas',
  'clients.list': 'Clientes',
  'meetings.list': 'Agenda',
  'meetings.create': 'Agenda',
  'people.list': 'Equipo',
  'finance.list_movements': 'Movimientos',
  'finance.list_pending': 'Cobros y pagos',
  'finance.compare_months': 'Comparación mensual',
  'my_day.get': 'Mi día',
  'projects.get': 'Proyecto',
  'clients.get': 'Cliente',
  'tasks.update': 'Tareas',
  'meetings.update': 'Agenda',
  'meetings.cancel': 'Agenda',
  'finance.create_movement': 'Finanzas',
  'clients.create_prospect': 'Clientes'
};

interface Suggestion {
  icon: string;
  text: string;
  /** Si es true, la sugerencia se copia al cuadro de texto para completarla en lugar de enviarse. */
  prefill?: boolean;
}

@Component({
  selector: 'app-intelligent-office-page',
  standalone: true,
  imports: [AiActionCardComponent, AiBriefingComponent, AiMessageExtrasComponent, AiPreferencesComponent, AiVoiceButtonComponent, DatePipe, FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, RouterLink],
  template: `
    <section class="office-page">
      <mat-card class="hero-card">
        <div class="hero-icon" aria-hidden="true"><mat-icon>auto_awesome</mat-icon></div>
        <div class="hero-text">
          <p class="eyebrow">Kodvian Core</p>
          <h2>Oficina Inteligente</h2>
          <p class="hero-subtitle">Consultá datos con fuentes verificables y pedí acciones como agendar tareas o reuniones. Nada se crea sin tu confirmación.</p>
        </div>
        @if (isAdministrator) { <a mat-stroked-button class="provider-link" routerLink="/administracion/configuracion-ia"><mat-icon>tune</mat-icon> Configuración del proveedor</a> }
      </mat-card>

      @if (pageError) { <p class="banner error" role="alert"><mat-icon>error_outline</mat-icon>{{ pageError }}</p> }
      @if (pageMessage) { <p class="banner notice" role="status"><mat-icon>check_circle</mat-icon>{{ pageMessage }}</p> }

      <div class="workspace">
        <mat-card class="history-panel">
          <button mat-flat-button color="primary" class="new-button" type="button" (click)="newConversation()" [disabled]="busy"><mat-icon>add</mat-icon> Nueva conversación</button>
          <h3 class="history-title">Conversaciones</h3>
          <input class="history-search" type="search" name="search" [(ngModel)]="search" (ngModelChange)="onSearchChange()" placeholder="Buscar conversaciones…" aria-label="Buscar conversaciones" maxlength="100" />
          @if (loadingList) { <mat-spinner diameter="28" aria-label="Cargando conversaciones" /> }
          <div class="conversation-list">
            @for (item of conversations; track item.id) {
              <div class="conversation-row" [class.selected]="item.id === activeConversation?.id">
                <button type="button" class="conversation-item" (click)="open(item)" [disabled]="busy">
                  <mat-icon class="conversation-icon">{{ item.isPinned ? 'push_pin' : 'chat_bubble_outline' }}</mat-icon>
                  <span class="conversation-body">
                    <span class="conversation-title">{{ item.title }}</span>
                    <span class="conversation-meta">{{ item.updatedAtUtc | date:'dd/MM HH:mm' }} · {{ item.messageCount }} mensajes</span>
                  </span>
                </button>
                <button type="button" class="pin-button" [class.pinned]="item.isPinned" [attr.aria-label]="item.isPinned ? 'Desfijar conversación' : 'Fijar conversación'"
                  [matTooltip]="item.isPinned ? 'Desfijar' : 'Fijar arriba'" (click)="togglePin(item)" [disabled]="busy"><mat-icon>push_pin</mat-icon></button>
              </div>
            } @empty { @if (!loadingList) { <p class="muted empty-history">{{ search.trim() ? 'No hay conversaciones que coincidan.' : 'Todavía no hay conversaciones. Escribí tu primera consulta a la derecha.' }}</p> } }
          </div>
          @if (conversations.length < totalConversations) { <button mat-button type="button" (click)="loadMoreConversations()" [disabled]="loadingList || busy">Cargar más</button> }
        </mat-card>

        <mat-card class="chat-panel">
          <div class="chat-header">
            <div class="chat-heading">
              @if (renaming && activeConversation) {
                <input class="rename-input" name="title" [(ngModel)]="titleDraft" maxlength="120" aria-label="Nuevo título" (keydown.enter)="saveTitle()" (keydown.escape)="renaming = false" (blur)="saveTitle()" />
              } @else {
                <h3>
                  {{ activeConversation?.title ?? 'Nueva conversación' }}
                  @if (activeConversation) { <button type="button" class="rename-button" aria-label="Renombrar conversación" matTooltip="Renombrar" (click)="startRename()"><mat-icon>edit</mat-icon></button> }
                </h3>
              }
              <p class="muted">Las conversaciones se conservan hasta que las elimines.</p>
            </div>
            <div class="chat-actions">
              <button mat-icon-button type="button" class="preferences-button" aria-label="Preferencias del asistente" matTooltip="Preferencias del asistente" (click)="showPreferences = !showPreferences"><mat-icon>tune</mat-icon></button>
              @if (activeConversation) {
                <button mat-icon-button type="button" class="delete-button" aria-label="Eliminar conversación" matTooltip="Eliminar conversación" (click)="deleteConversation()" [disabled]="busy"><mat-icon>delete_outline</mat-icon></button>
              }
            </div>
          </div>
          @if (showPreferences) { <app-ai-preferences (closed)="showPreferences = false" /> }

          @if (chatError) {
            <div class="banner error" role="alert">
              <mat-icon>error_outline</mat-icon><span>{{ chatError }}</span>
              @if (hasPendingRequest) { <button mat-button type="button" (click)="retryAsNewRun()" [disabled]="busy">Reintentar</button> }
            </div>
          }

          <div class="messages" #messageList aria-live="polite">
            @if (activeConversation && messages.length < activeConversation.messageCount) { <p class="muted history-note">Se muestran los últimos 200 mensajes de {{ activeConversation.messageCount }}.</p> }
            @for (message of messages; track message.id) {
              <article class="message" [class.user-message]="message.role === 'user'" [class.assistant-message]="message.role === 'assistant'">
                @if (message.role === 'assistant') { <div class="avatar" aria-hidden="true"><mat-icon>auto_awesome</mat-icon></div> }
                <div class="bubble">
                  <div class="message-label">
                    <span>{{ message.role === 'user' ? 'Vos' : 'Oficina Inteligente' }}</span>
                    <time [attr.title]="message.traceId ? 'Seguimiento ' + message.traceId : null">{{ message.createdAtUtc | date:'dd/MM HH:mm' }}</time>
                  </div>
                  @if (message.role === 'assistant') {
                    <div class="message-content markdown" [innerHTML]="renderMarkdown(message.content)"></div>
                  } @else {
                    <p class="message-content">{{ message.content }}</p>
                  }
                  @if (actionableCount(message) > 1) {
                    <div class="batch-bar">
                      <span>{{ actionableCount(message) }} acciones listas</span>
                      <button mat-flat-button color="primary" type="button" class="confirm-all" (click)="confirmAll(message)" [disabled]="busy || !!actionInProgress">
                        @if (actionInProgress === message.id) { <mat-spinner diameter="16" /> } @else { <mat-icon>done_all</mat-icon> }
                        Confirmar todas
                      </button>
                    </div>
                  }
                  @for (action of message.pendingActions ?? []; track action.id) {
                    <app-ai-action-card [action]="action" [disabled]="busy || !!actionInProgress" [inProgress]="actionInProgress === action.id"
                      (confirm)="confirmAction($event)" (cancel)="cancelAction($event)" (undo)="undoAction($event)" />
                  }
                  @if (message.sources.length) {
                    <div class="sources">
                      <span class="sources-title"><mat-icon>verified</mat-icon> Fuentes</span>
                      @for (source of message.sources; track $index) {
                        @if (source.link) {
                          <a class="source-chip" [href]="source.link" [attr.title]="sourceDetail(source)">{{ sourceLabel(source) }}</a>
                        } @else {
                          <span class="source-chip" [attr.title]="sourceDetail(source)">{{ sourceLabel(source) }}</span>
                        }
                      }
                    </div>
                  } @else if (message.role === 'assistant' && message.grounded === false) {
                    <p class="ungrounded" matTooltip="Esta respuesta no consultó datos de Kodvian.">
                      <mat-icon>info_outline</mat-icon> Sin datos consultados
                    </p>
                  }
                  @if (message.role === 'assistant') { <app-ai-message-extras [message]="message" (rate)="rate(message, $event)" /> }
                </div>
              </article>
            } @empty {
              @if (!pendingContent) {
                <div class="empty-chat">
                  @if (briefing) { <app-ai-briefing [briefing]="briefing" (ask)="useFollowup($event)" /> }
                  <div class="empty-icon"><mat-icon>auto_awesome</mat-icon></div>
                  <h4>¿En qué te ayudo hoy?</h4>
                  <p class="muted">Preguntá por tareas, proyectos, reuniones, clientes o finanzas, o pedime que agende algo. Cada respuesta indica de dónde salió el dato y las acciones esperan tu confirmación.</p>
                  <div class="suggestions">
                    @for (suggestion of suggestions; track suggestion.text) {
                      <button type="button" class="suggestion" (click)="useSuggestion(suggestion)" [disabled]="busy">
                        <mat-icon>{{ suggestion.icon }}</mat-icon><span>{{ suggestion.text }}</span>
                      </button>
                    }
                  </div>
                </div>
              }
            }
            @if (pendingContent) {
              <article class="message user-message pending">
                <div class="bubble">
                  <div class="message-label"><span>Vos</span></div>
                  <p class="message-content">{{ pendingContent }}</p>
                </div>
              </article>
              <article class="message assistant-message">
                <div class="avatar" aria-hidden="true"><mat-icon>auto_awesome</mat-icon></div>
                @if (visibleStreamingText) {
                  <div class="bubble streaming">
                    <div class="message-label"><span>Oficina Inteligente</span></div>
                    <div class="message-content markdown" [innerHTML]="renderMarkdown(visibleStreamingText)"></div>
                    <span class="stream-cursor" aria-hidden="true"></span>
                  </div>
                } @else {
                  <div class="bubble thinking" role="status">
                    <span class="dot"></span><span class="dot"></span><span class="dot"></span>
                    <span class="progress-label">{{ progressLabel || 'Pensando…' }}</span>
                  </div>
                }
              </article>
            }
            @if (!busy && lastAssistantSuggestions.length) {
              <div class="followups" aria-label="Sugerencias para seguir">
                @for (suggestion of lastAssistantSuggestions; track suggestion) {
                  <button type="button" class="followup" (click)="useFollowup(suggestion)"><mat-icon>subdirectory_arrow_right</mat-icon>{{ suggestion }}</button>
                }
              </div>
            }
          </div>

          <form class="composer" (ngSubmit)="send()">
            <div class="composer-box" [class.disabled]="busy">
              <textarea #composerInput name="message" [(ngModel)]="draft" maxlength="4000" rows="2" [disabled]="busy" (keydown)="onComposerKeydown($event)"
                aria-label="Escribí tu consulta" placeholder="Escribí tu consulta…"></textarea>
              <app-ai-voice-button [disabled]="busy" (transcript)="onVoice($event)" (failed)="chatError = $event" />
              <button mat-flat-button color="primary" class="send-button" type="submit" [disabled]="busy || !draft.trim()" aria-label="Consultar">
                @if (busy && pendingContent) { <mat-spinner diameter="18" /> } @else { <mat-icon>send</mat-icon> }
                <span>Consultar</span>
              </button>
            </div>
            <div class="composer-hint"><span>Enter para enviar · Shift + Enter para nueva línea</span><span>{{ draft.length }} / 4000</span></div>
          </form>
        </mat-card>
      </div>
    </section>
  `,
  styles: [`
    .office-page { display: grid; gap: 12px; min-width: 0; }
    .hero-card, .history-panel, .chat-panel { border: 1px solid var(--kd-border-subtle); border-radius: 16px; background: linear-gradient(180deg, rgba(17,26,36,.94), rgba(13,21,30,.98)); color: var(--text-100); }
    mat-icon { flex-shrink: 0; }

    .hero-card { display: flex; flex-direction: row; align-items: center; gap: 16px; padding: 18px 20px; }
    .hero-icon { display: grid; place-items: center; width: 52px; height: 52px; border-radius: 14px; color: var(--brand-300); background: rgba(34, 197, 94, .1); border: 1px solid rgba(34, 197, 94, .25); }
    .hero-icon mat-icon { width: 28px; height: 28px; font-size: 28px; }
    .hero-text { flex: 1; min-width: 0; }
    .eyebrow { margin: 0 0 2px; color: var(--brand-300); font-size: 11px; letter-spacing: 1.2px; text-transform: uppercase; }
    h2 { margin: 0; color: var(--text-100); font-size: 24px; line-height: 1.2; }
    .hero-subtitle { margin: 4px 0 0; color: var(--text-300); font-size: 13px; }
    .provider-link { flex-shrink: 0; }

    .banner { display: flex; align-items: center; gap: 8px; margin: 0; padding: 10px 12px; border-radius: 10px; font-size: 13px; }
    .banner span { flex: 1; }
    .error { color: #ffb4b4; background: rgba(255, 99, 99, .08); border: 1px solid rgba(255, 99, 99, .25); }
    .notice { color: var(--brand-300); background: rgba(34, 197, 94, .08); border: 1px solid rgba(34, 197, 94, .25); }

    .workspace { display: grid; grid-template-columns: minmax(220px, 280px) minmax(0, 1fr); gap: 12px; height: calc(100vh - 290px); min-height: 520px; }
    .history-panel { display: flex; flex-direction: column; gap: 10px; padding: 12px; min-height: 0; }
    .new-button { width: 100%; }
    .history-title { margin: 6px 4px 0; font-size: 11px; letter-spacing: 1px; text-transform: uppercase; color: var(--text-300); }
    .conversation-list { display: grid; align-content: start; gap: 4px; flex: 1; min-height: 0; overflow-y: auto; }
    .conversation-item { display: flex; flex: 1; min-width: 0; align-items: flex-start; gap: 10px; padding: 9px 10px; text-align: left; color: var(--text-100); background: transparent; border: 0; border-radius: 10px; cursor: pointer; font: inherit; }
    .conversation-item:hover:not(:disabled) { background: rgba(255,255,255,.04); }
    .conversation-row { display: flex; align-items: center; border: 1px solid transparent; border-radius: 10px; }
    .conversation-row.selected { border-color: rgba(34, 197, 94, .35); background: rgba(34, 197, 94, .08); }
    .pin-button { display: grid; place-items: center; width: 28px; height: 28px; margin-right: 4px; padding: 0; border: 0; border-radius: 6px; background: transparent; color: var(--text-300); cursor: pointer; opacity: 0; }
    .conversation-row:hover .pin-button, .pin-button.pinned, .pin-button:focus-visible { opacity: 1; }
    .pin-button.pinned { color: var(--brand-300); }
    .pin-button mat-icon, .rename-button mat-icon { width: 16px; height: 16px; font-size: 16px; }
    .history-search, .rename-input { width: 100%; box-sizing: border-box; padding: 7px 10px; border-radius: 8px; border: 1px solid var(--kd-border-subtle); background: rgba(0,0,0,.2); color: var(--text-100); font: inherit; font-size: 13px; }
    .rename-button { display: inline-grid; place-items: center; width: 24px; height: 24px; padding: 0; border: 0; background: transparent; color: var(--text-300); cursor: pointer; vertical-align: middle; }
    .conversation-item:disabled { cursor: default; }
    .conversation-icon { width: 18px; height: 18px; font-size: 18px; margin-top: 1px; color: var(--text-300); }
    .conversation-row.selected .conversation-icon { color: var(--brand-300); }
    .conversation-body { display: grid; gap: 3px; min-width: 0; }
    .conversation-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; }
    .conversation-meta { color: var(--text-300); font-size: 11px; }
    .empty-history { margin: 4px; font-size: 13px; }

    .chat-panel { display: flex; flex-direction: column; min-width: 0; min-height: 0; padding: 0; overflow: hidden; }
    .chat-header { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 14px 18px; border-bottom: 1px solid var(--kd-border-subtle); }
    .chat-heading { min-width: 0; }
    .chat-heading h3 { margin: 0; font-size: 15px; color: var(--text-100); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .chat-heading p { margin: 3px 0 0; font-size: 12px; }
    .chat-actions { display: flex; gap: 2px; flex-shrink: 0; }
    .delete-button, .preferences-button { color: var(--text-300); }
    .delete-button:hover { color: #ffb4b4; }
    .chat-panel > .banner { margin: 12px 18px 0; }
    .muted { color: var(--text-300); }

    .messages { display: flex; flex: 1; flex-direction: column; gap: 16px; min-height: 0; padding: 18px; overflow-y: auto; scroll-behavior: smooth; }
    .history-note { align-self: center; margin: 0; font-size: 12px; }
    .message { display: flex; gap: 10px; max-width: min(820px, 88%); }
    .user-message { align-self: flex-end; }
    .assistant-message { align-self: flex-start; }
    .avatar { display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%; flex-shrink: 0; color: var(--brand-300); background: rgba(34, 197, 94, .12); }
    .avatar mat-icon { width: 17px; height: 17px; font-size: 17px; }
    .bubble { min-width: 0; padding: 10px 14px; border-radius: 14px; overflow-wrap: anywhere; }
    .user-message .bubble { background: rgba(34, 197, 94, .14); border: 1px solid rgba(34, 197, 94, .28); border-bottom-right-radius: 4px; }
    .assistant-message .bubble { background: rgba(255,255,255,.035); border: 1px solid var(--kd-border-subtle); border-top-left-radius: 4px; }
    .pending .bubble { opacity: .75; }
    .message-label { display: flex; justify-content: space-between; gap: 16px; color: var(--text-300); font-size: 11px; font-weight: 600; }
    time { color: var(--text-300); font-size: 11px; font-weight: 400; }
    .message-content { margin: 6px 0 0; white-space: pre-wrap; line-height: 1.55; font-size: 14px; color: var(--text-100); }
    .sources { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; margin-top: 10px; padding-top: 8px; border-top: 1px solid var(--kd-border-subtle); }
    .sources-title { display: inline-flex; align-items: center; gap: 4px; color: var(--text-300); font-size: 11px; font-weight: 600; }
    .sources-title mat-icon { width: 14px; height: 14px; font-size: 14px; color: var(--brand-300); }
    .source-chip { padding: 3px 9px; border-radius: 999px; font-size: 11px; color: var(--text-100); background: rgba(255,255,255,.05); border: 1px solid var(--kd-border-subtle); text-decoration: none; }
    a.source-chip { color: var(--brand-300); }
    a.source-chip:hover { border-color: rgba(34, 197, 94, .45); }
    .markdown :is(p, ul, ol) { margin: 0 0 8px; }
    .markdown > :last-child { margin-bottom: 0; }
    .markdown :is(ul, ol) { padding-left: 20px; }
    .markdown li { margin: 2px 0; }
    .markdown .md-heading { margin-top: 4px; font-weight: 700; }
    .markdown code { padding: 1px 5px; border-radius: 5px; background: rgba(255,255,255,.07); font-size: 12.5px; }
    .markdown .md-table { overflow-x: auto; margin: 4px 0 8px; }
    .markdown table { border-collapse: collapse; font-size: 13px; }
    .markdown :is(th, td) { padding: 5px 10px; border: 1px solid var(--kd-border-subtle); text-align: left; white-space: nowrap; }
    .markdown th { color: var(--text-300); font-weight: 600; background: rgba(255,255,255,.03); }
    .assistant-message .message-content.markdown { white-space: normal; }


    .batch-bar { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-top: 10px; padding: 8px 10px; border-radius: 10px; font-size: 13px; background: rgba(34, 197, 94, .08); }
    .batch-bar mat-spinner { display: inline-block; margin-right: 6px; }
    .ungrounded { display: inline-flex; align-items: center; gap: 4px; margin: 8px 0 0; color: var(--text-300); font-size: 11px; }
    .ungrounded mat-icon { width: 14px; height: 14px; font-size: 14px; }

    .thinking { display: flex; align-items: center; gap: 5px; padding: 14px 16px; }
    .progress-label { margin-left: 6px; color: var(--text-300); font-size: 12px; }
    .stream-cursor { display: inline-block; width: 7px; height: 14px; margin-top: 4px; background: var(--brand-300); animation: blink 1s infinite; }
    .followups { display: flex; flex-wrap: wrap; gap: 6px; margin-left: 40px; }
    .followup { display: inline-flex; align-items: center; gap: 4px; padding: 6px 11px; border-radius: 999px; font: inherit; font-size: 12px; color: var(--brand-300); background: rgba(34, 197, 94, .06); border: 1px solid rgba(34, 197, 94, .3); cursor: pointer; }
    .followup:hover { background: rgba(34, 197, 94, .12); }
    .followup mat-icon { width: 15px; height: 15px; font-size: 15px; }
    .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--brand-300); animation: blink 1.2s infinite ease-in-out; }
    .dot:nth-child(2) { animation-delay: .2s; }
    .dot:nth-child(3) { animation-delay: .4s; }
    @keyframes blink { 0%, 80%, 100% { opacity: .25; transform: translateY(0); } 40% { opacity: 1; transform: translateY(-3px); } }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }

    .empty-chat { display: flex; flex-direction: column; align-items: center; margin: auto; max-width: 620px; text-align: center; }
    .empty-icon { display: grid; place-items: center; width: 56px; height: 56px; margin-bottom: 12px; border-radius: 50%; color: var(--brand-300); background: rgba(34, 197, 94, .1); }
    .empty-icon mat-icon { width: 28px; height: 28px; font-size: 28px; }
    .empty-chat h4 { margin: 0; font-size: 18px; color: var(--text-100); }
    .empty-chat > p { margin: 6px 0 18px; font-size: 13px; }
    .suggestions { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; width: 100%; }
    .suggestion { display: flex; align-items: center; gap: 10px; padding: 11px 12px; text-align: left; font: inherit; font-size: 13px; color: var(--text-100); background: rgba(255,255,255,.03); border: 1px solid var(--kd-border-subtle); border-radius: 12px; cursor: pointer; transition: border-color .15s, background .15s; }
    .suggestion:hover:not(:disabled) { border-color: rgba(34, 197, 94, .45); background: rgba(34, 197, 94, .06); }
    .suggestion mat-icon { width: 20px; height: 20px; font-size: 20px; color: var(--brand-300); }

    .composer { padding: 12px 18px 14px; border-top: 1px solid var(--kd-border-subtle); }
    .composer-box { display: flex; align-items: flex-end; gap: 10px; padding: 8px 8px 8px 14px; border: 1px solid var(--kd-border-subtle); border-radius: 14px; background: rgba(0,0,0,.18); transition: border-color .15s; }
    .composer-box:focus-within { border-color: rgba(34, 197, 94, .55); }
    .composer-box.disabled { opacity: .7; }
    .composer-box textarea { flex: 1; min-height: 40px; max-height: 160px; padding: 9px 0; resize: none; border: 0; outline: 0; background: transparent; color: var(--text-100); font: inherit; font-size: 14px; line-height: 1.45; field-sizing: content; }
    .composer-box textarea::placeholder { color: var(--text-300); }
    .send-button { flex-shrink: 0; height: 40px; border-radius: 10px; }
    .send-button:disabled { background: rgba(255,255,255,.06) !important; color: var(--text-300) !important; }
    .send-button mat-spinner { display: inline-block; margin-right: 6px; }
    .composer-hint { display: flex; justify-content: space-between; gap: 12px; margin-top: 6px; padding: 0 4px; color: var(--text-300); font-size: 11px; }

    @media (max-width: 760px) {
      .hero-card { flex-wrap: wrap; }
      .provider-link { width: 100%; }
      .workspace { grid-template-columns: 1fr; height: auto; }
      .history-panel { max-height: 240px; }
      .messages { min-height: 360px; max-height: 60vh; }
      .message { max-width: 100%; }
      .suggestions { grid-template-columns: 1fr; }
      .composer-hint span:first-child { display: none; }
    }
  `]
})
export class IntelligentOfficePageComponent implements OnInit, AfterViewChecked {
  private readonly api = inject(IntelligentOfficeService);
  /** La configuración del proveedor y el uso de IA son solo para Administradores. */
  readonly isAdministrator = inject(AuthSessionService).user?.roles.includes('Administrador') ?? false;
  @ViewChild('messageList') private messageList?: ElementRef<HTMLElement>;
  private shouldScroll = false;

  readonly suggestions: Suggestion[] = [
    { icon: 'today', text: '¿Qué tengo hoy?' },
    { icon: 'add_task', text: 'Agendá una tarea para mañana en el proyecto ', prefill: true },
    { icon: 'calendar_month', text: '¿Qué reuniones tengo esta semana?' },
    { icon: 'event', text: 'Agendá una reunión el jueves a las 10 con ', prefill: true },
    { icon: 'compare_arrows', text: 'Compará los gastos de este mes con el anterior' },
    { icon: 'request_quote', text: '¿Quién me debe?' }
  ];

  actionInProgress: string | null = null;
  progressLabel = '';
  streamingText = '';

  /** Texto en vivo sin la línea de sugerencias, que llega al final y se muestra como botones. */
  get visibleStreamingText(): string {
    const marker = this.streamingText.search(/\[\[\s*sugerencias/i);
    const text = marker >= 0 ? this.streamingText.slice(0, marker) : this.streamingText.replace(/\[{1,2}[^\]]{0,14}$/, '');
    return text.trim();
  }

  /** Sugerencias de seguimiento de la última respuesta del asistente. */
  get lastAssistantSuggestions(): string[] {
    const last = this.messages[this.messages.length - 1];
    return last?.role === 'assistant' ? last.suggestions ?? [] : [];
  }

  useFollowup(text: string): void {
    this.draft = text;
    this.send();
  }

  private resetStreaming(): void {
    this.progressLabel = '';
    this.streamingText = '';
  }
  private readonly markdownCache = new Map<string, string>();
  @ViewChild('composerInput') private composerInput?: ElementRef<HTMLTextAreaElement>;

  conversations: AiConversation[] = [];
  activeConversation?: AiConversation;
  messages: AiMessage[] = [];
  draft = '';
  pendingContent = '';
  loadingList = true;
  totalConversations = 0;
  private conversationPage = 1;
  busy = false;
  pageError = '';
  pageMessage = '';
  chatError = '';
  private pendingRequest: { content: string; id: string } | null = null;

  briefing: AiBriefing | null = null;
  showPreferences = false;
  search = '';
  renaming = false;
  titleDraft = '';
  private searchTimer?: ReturnType<typeof setTimeout>;
  private voiceBase: string | null = null;
  /** Pregunta y pantalla de origen cuando se abre desde el lanzador rápido (Ctrl+K). */
  private quickQuestion: string | null = null;
  private pendingPageContext: string | null = null;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  ngOnInit(): void {
    const params = this.route.snapshot?.queryParamMap;
    const question = params?.get('q')?.trim();
    if (question) {
      this.quickQuestion = question.slice(0, 4000);
      this.pendingPageContext = params?.get('ctx') ?? null;
      // Se limpia la URL para que recargar la página no repita la pregunta.
      void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
    this.loadConversations();
    this.loadBriefing();
  }

  /** El resumen es opcional: si falla, la pantalla funciona igual sin él. */
  loadBriefing(): void {
    this.api.briefing().subscribe({ next: briefing => this.briefing = briefing, error: () => this.briefing = null });
  }

  ngAfterViewChecked(): void {
    if (!this.shouldScroll || !this.messageList) return;
    this.shouldScroll = false;
    const element = this.messageList.nativeElement;
    element.scrollTop = element.scrollHeight;
  }

  get hasPendingRequest(): boolean { return this.pendingRequest !== null; }

  sourceLabel(source: AiSourceReference): string { return SOURCE_LABELS[source.toolName] ?? source.toolName; }

  sourceDetail(source: AiSourceReference): string {
    const parts = [source.from && source.to ? `${source.from} a ${source.to}` : '', source.currency ?? '', `Dato al ${new Date(source.asOfUtc).toLocaleString('es-AR')}`];
    return parts.filter(Boolean).join(' · ');
  }

  useSuggestion(suggestion: Suggestion): void {
    this.draft = suggestion.text;
    if (!suggestion.prefill) { this.send(); return; }
    // Las sugerencias de acción se completan antes de enviar (proyecto, persona, horario).
    setTimeout(() => {
      const input = this.composerInput?.nativeElement;
      if (!input) return;
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
    });
  }

  renderMarkdown(content: string): string {
    let html = this.markdownCache.get(content);
    if (html === undefined) {
      html = renderAiMarkdown(content);
      if (this.markdownCache.size > 300) this.markdownCache.clear();
      this.markdownCache.set(content, html);
    }
    return html;
  }

  actionableCount(message: AiMessage): number { return (message.pendingActions ?? []).filter(isActionable).length; }

  confirmAction(action: AiPendingAction): void { this.resolveAction(action, 'confirm'); }

  cancelAction(action: AiPendingAction): void { this.resolveAction(action, 'cancel'); }

  undoAction(action: AiPendingAction): void { this.resolveAction(action, 'undo'); }

  confirmAll(message: AiMessage): void {
    const conversation = this.activeConversation;
    if (!conversation || this.busy || this.actionInProgress) return;
    this.actionInProgress = message.id;
    this.chatError = '';
    this.api.confirmAll(conversation.id, message.id).pipe(finalize(() => this.actionInProgress = null)).subscribe({
      next: result => {
        result.actions.forEach(action => this.replaceAction(action));
        if (result.message) this.appendSystemMessage(conversation, result.message);
      },
      error: error => { this.chatError = IntelligentOfficeService.errorMessage(error); this.reloadActive(); }
    });
  }

  private resolveAction(action: AiPendingAction, kind: 'confirm' | 'cancel' | 'undo'): void {
    const conversation = this.activeConversation;
    if (!conversation || this.busy || this.actionInProgress) return;
    this.actionInProgress = action.id;
    this.chatError = '';
    const request = kind === 'confirm' ? this.api.confirmAction(conversation.id, action.id)
      : kind === 'undo' ? this.api.undoAction(conversation.id, action.id)
      : this.api.cancelAction(conversation.id, action.id);
    request.pipe(finalize(() => this.actionInProgress = null)).subscribe({
      next: result => {
        this.replaceAction(result.action);
        if (result.message) this.appendSystemMessage(conversation, result.message);
      },
      error: error => { this.chatError = IntelligentOfficeService.errorMessage(error); this.reloadActive(); }
    });
  }

  private replaceAction(action: AiPendingAction): void {
    this.messages = this.messages.map(message => message.pendingActions?.some(x => x.id === action.id)
      ? { ...message, pendingActions: message.pendingActions!.map(x => x.id === action.id ? action : x) }
      : message);
  }

  private appendSystemMessage(conversation: AiConversation, message: AiMessage): void {
    this.messages = [...this.messages, message];
    this.activeConversation = { ...conversation, messageCount: conversation.messageCount + 1, updatedAtUtc: message.createdAtUtc };
    this.conversations = this.conversations.map(x => x.id === conversation.id ? this.activeConversation! : x);
    this.shouldScroll = true;
  }

  onComposerKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return;
    event.preventDefault();
    this.send();
  }

  loadConversations(reset = true, openFirst = true): void {
    this.loadingList = true;
    this.pageError = '';
    const requestedPage = reset ? 1 : this.conversationPage + 1;
    this.api.list(requestedPage, this.search).pipe(finalize(() => this.loadingList = false)).subscribe({
      next: result => {
        this.conversationPage = requestedPage;
        this.totalConversations = result.totalCount;
        this.conversations = reset ? result.items : [...this.conversations, ...result.items];
        // Si se llegó con una pregunta rápida (Ctrl+K desde otra pantalla), se arranca una conversación nueva con ella.
        if (reset && this.quickQuestion) { this.askQuickQuestion(); return; }
        if (reset && openFirst && this.conversations.length) this.open(this.conversations[0]);
      },
      error: () => this.pageError = 'No se pudo cargar el historial de conversaciones.'
    });
  }

  onSearchChange(): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadConversations(true, false), 300);
  }

  togglePin(conversation: AiConversation): void {
    this.api.update(conversation.id, { isPinned: !conversation.isPinned }).subscribe({
      next: updated => {
        const merged = { ...conversation, ...updated };
        const others = this.conversations.filter(x => x.id !== conversation.id);
        this.conversations = merged.isPinned ? [merged, ...others] : [...others.filter(x => x.isPinned), merged, ...others.filter(x => !x.isPinned)];
        if (this.activeConversation?.id === conversation.id) this.activeConversation = merged;
      },
      error: () => this.pageError = 'No se pudo fijar la conversación.'
    });
  }

  startRename(): void {
    if (!this.activeConversation) return;
    this.titleDraft = this.activeConversation.title;
    this.renaming = true;
  }

  saveTitle(): void {
    const conversation = this.activeConversation;
    const title = this.titleDraft.trim();
    if (!this.renaming || !conversation) return;
    this.renaming = false;
    if (!title || title === conversation.title) return;
    this.api.update(conversation.id, { title }).subscribe({
      next: updated => {
        this.activeConversation = { ...conversation, ...updated };
        this.conversations = this.conversations.map(x => x.id === conversation.id ? this.activeConversation! : x);
      },
      error: () => this.chatError = 'No se pudo renombrar la conversación.'
    });
  }

  rate(message: AiMessage, value: number): void {
    const conversation = this.activeConversation;
    if (!conversation) return;
    const previous = message.feedback ?? null;
    this.messages = this.messages.map(x => x.id === message.id ? { ...x, feedback: value === 0 ? null : value } : x);
    this.api.feedback(conversation.id, message.id, value).subscribe({
      error: () => this.messages = this.messages.map(x => x.id === message.id ? { ...x, feedback: previous } : x)
    });
  }

  onVoice(result: { text: string; final: boolean }): void {
    // El dictado se agrega a lo que ya estaba escrito; los resultados parciales se van reemplazando.
    if (this.voiceBase === null) this.voiceBase = this.draft.trim();
    this.draft = [this.voiceBase, result.text].filter(Boolean).join(' ');
    if (result.final) this.voiceBase = null;
  }

  private askQuickQuestion(): void {
    const question = this.quickQuestion;
    this.quickQuestion = null;
    if (!question) return;
    this.activeConversation = undefined;
    this.messages = [];
    this.draft = question;
    this.send();
  }

  loadMoreConversations(): void { if (!this.loadingList && this.conversations.length < this.totalConversations) this.loadConversations(false); }

  open(conversation: AiConversation): void {
    if (this.busy) return;
    this.busy = true;
    this.chatError = '';
    this.api.get(conversation.id).pipe(finalize(() => this.busy = false)).subscribe({
      next: detail => {
        this.activeConversation = { ...conversation, title: detail.title, updatedAtUtc: detail.updatedAtUtc, messageCount: detail.messageCount };
        this.conversations = this.conversations.map(x => x.id === conversation.id ? this.activeConversation! : x);
        this.messages = detail.messages;
        this.shouldScroll = true;
      },
      error: () => this.chatError = 'No se pudo abrir la conversación.'
    });
  }

  newConversation(): void {
    if (this.busy) return;
    this.busy = true;
    this.chatError = '';
    this.api.create().pipe(finalize(() => this.busy = false)).subscribe({
      next: conversation => {
        this.activeConversation = conversation;
        this.messages = [];
        this.conversations = [conversation, ...this.conversations];
        this.totalConversations++;
        this.pageMessage = '';
      },
      error: () => this.chatError = 'No se pudo crear una conversación.'
    });
  }

  send(): void {
    const content = this.draft.trim();
    if (!content || this.busy) return;
    this.busy = true;
    this.chatError = '';
    this.pageMessage = '';
    this.pendingContent = content;
    this.draft = '';
    this.shouldScroll = true;
    const fail = (message: string): void => {
      this.chatError = message;
      this.draft = content;
      this.pendingContent = '';
    };
    const submit = (conversation: AiConversation): void => {
      this.activeConversation = conversation;
      if (!this.pendingRequest || this.pendingRequest.content !== content)
        this.pendingRequest = { content, id: globalThis.crypto?.randomUUID?.() ?? 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, character => {
          const random = Math.floor(Math.random() * 16);
          return (character === 'x' ? random : (random & 0x3) | 0x8).toString(16);
        }) };
      const clientRequestId = this.pendingRequest.id;
      const pageContext = this.pendingPageContext;
      this.pendingPageContext = null;
      const applyResult = (result: AiSendResult): void => {
          this.pendingRequest = null;
          this.pendingContent = '';
          this.resetStreaming();
          const userWasLoaded = this.messages.some(x => x.id === result.userMessage.id);
          const assistantWasLoaded = this.messages.some(x => x.id === result.assistantMessage.id);
          this.messages = [...this.messages.filter(x => x.id !== result.userMessage.id && x.id !== result.assistantMessage.id), result.userMessage, result.assistantMessage];
          this.conversations = this.conversations.map(x => x.id === conversation.id
            ? { ...x, title: x.title === 'Nueva conversación' ? content.slice(0, 120) : x.title, updatedAtUtc: result.assistantMessage.createdAtUtc, messageCount: x.messageCount + (userWasLoaded ? 0 : 1) + (assistantWasLoaded ? 0 : 1) }
            : x);
          this.activeConversation = this.conversations.find(x => x.id === conversation.id) ?? this.activeConversation;
          this.shouldScroll = true;
      };
      const onError = (error: unknown): void => { this.resetStreaming(); fail(IntelligentOfficeService.errorMessage(error)); this.reloadActive(); };
      const sendWithoutStreaming = (): void => {
        this.api.send(conversation.id, content, clientRequestId, pageContext).pipe(finalize(() => this.busy = false)).subscribe({ next: applyResult, error: onError });
      };

      // Respuesta en vivo; si el streaming no está disponible se usa el envío común con el mismo identificador.
      this.api.sendStream(conversation.id, content, clientRequestId, pageContext).subscribe({
        next: event => {
          switch (event.type) {
            case 'status': this.progressLabel = event.data.label; break;
            case 'delta': this.streamingText += event.data.text; this.shouldScroll = true; break;
            case 'reset': this.streamingText = ''; break;
            case 'done': this.busy = false; applyResult(event.data); break;
          }
        },
        error: error => {
          if (error instanceof StreamUnavailableError) { this.resetStreaming(); sendWithoutStreaming(); return; }
          this.busy = false;
          onError(error);
        }
      });
    };

    if (this.activeConversation) submit(this.activeConversation);
    else this.api.create().subscribe({
      next: conversation => { this.conversations = [conversation, ...this.conversations]; this.totalConversations++; submit(conversation); },
      error: () => { this.busy = false; fail('No se pudo iniciar la conversación.'); }
    });
  }

  retryAsNewRun(): void {
    this.pendingRequest = null;
    this.send();
  }

  deleteConversation(): void {
    const conversation = this.activeConversation;
    if (!conversation || this.busy || !globalThis.confirm('¿Eliminar esta conversación y sus mensajes?')) return;
    this.busy = true;
    this.chatError = '';
    this.api.delete(conversation.id).pipe(finalize(() => this.busy = false)).subscribe({
      next: () => {
        this.conversations = this.conversations.filter(x => x.id !== conversation.id);
        this.totalConversations = Math.max(0, this.totalConversations - 1);
        this.activeConversation = undefined;
        this.messages = [];
        this.pageMessage = 'Se eliminaron la conversación y sus mensajes. La metadata de auditoría se conserva hasta 90 días.';
      },
      error: () => this.chatError = 'No se pudo eliminar la conversación.'
    });
  }

  private reloadActive(): void {
    if (!this.activeConversation) return;
    this.api.get(this.activeConversation.id).subscribe({
      next: detail => { this.messages = detail.messages; this.shouldScroll = true; },
      error: () => undefined
    });
  }
}
