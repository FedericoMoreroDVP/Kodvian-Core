import { DatePipe } from '@angular/common';
import { AfterViewChecked, Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { renderAiMarkdown } from './ai-markdown';
import { AiConversation, AiMessage, AiPendingAction, AiSendResult, AiSourceReference } from './intelligent-office.models';
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
  'people.list': 'Equipo'
};

const ACTION_ICONS: Record<string, string> = {
  'tasks.create': 'add_task',
  'tasks.update_status': 'published_with_changes',
  'meetings.create': 'event'
};

const ACTION_STATUS_LABELS: Record<string, string> = {
  Pending: 'Pendiente de confirmación',
  Executing: 'Procesando…',
  Confirmed: 'Confirmada',
  Cancelled: 'Cancelada',
  Expired: 'Vencida',
  Failed: 'No se pudo completar'
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
  imports: [DatePipe, FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, RouterLink],
  template: `
    <section class="office-page">
      <mat-card class="hero-card">
        <div class="hero-icon" aria-hidden="true"><mat-icon>auto_awesome</mat-icon></div>
        <div class="hero-text">
          <p class="eyebrow">Kodvian Core</p>
          <h2>Oficina Inteligente</h2>
          <p class="hero-subtitle">Consultá datos con fuentes verificables y pedí acciones como agendar tareas o reuniones. Nada se crea sin tu confirmación.</p>
        </div>
        <a mat-stroked-button class="provider-link" routerLink="/administracion/configuracion-ia"><mat-icon>tune</mat-icon> Configuración del proveedor</a>
      </mat-card>

      @if (pageError) { <p class="banner error" role="alert"><mat-icon>error_outline</mat-icon>{{ pageError }}</p> }
      @if (pageMessage) { <p class="banner notice" role="status"><mat-icon>check_circle</mat-icon>{{ pageMessage }}</p> }

      <div class="workspace">
        <mat-card class="history-panel">
          <button mat-flat-button color="primary" class="new-button" type="button" (click)="newConversation()" [disabled]="busy"><mat-icon>add</mat-icon> Nueva conversación</button>
          <h3 class="history-title">Conversaciones</h3>
          @if (loadingList) { <mat-spinner diameter="28" aria-label="Cargando conversaciones" /> }
          <div class="conversation-list">
            @for (item of conversations; track item.id) {
              <button type="button" class="conversation-item" [class.selected]="item.id === activeConversation?.id" (click)="open(item)" [disabled]="busy">
                <mat-icon class="conversation-icon">chat_bubble_outline</mat-icon>
                <span class="conversation-body">
                  <span class="conversation-title">{{ item.title }}</span>
                  <span class="conversation-meta">{{ item.updatedAtUtc | date:'dd/MM HH:mm' }} · {{ item.messageCount }} mensajes</span>
                </span>
              </button>
            } @empty { @if (!loadingList) { <p class="muted empty-history">Todavía no hay conversaciones. Escribí tu primera consulta a la derecha.</p> } }
          </div>
          @if (conversations.length < totalConversations) { <button mat-button type="button" (click)="loadMoreConversations()" [disabled]="loadingList || busy">Cargar más</button> }
        </mat-card>

        <mat-card class="chat-panel">
          <div class="chat-header">
            <div class="chat-heading">
              <h3>{{ activeConversation?.title ?? 'Nueva conversación' }}</h3>
              <p class="muted">Las conversaciones se conservan hasta que las elimines.</p>
            </div>
            @if (activeConversation) {
              <button mat-icon-button type="button" class="delete-button" aria-label="Eliminar conversación" matTooltip="Eliminar conversación" (click)="deleteConversation()" [disabled]="busy"><mat-icon>delete_outline</mat-icon></button>
            }
          </div>

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
                  @for (action of message.pendingActions ?? []; track action.id) {
                    <section class="action-card" [attr.data-status]="action.status" [attr.aria-label]="action.title">
                      <header class="action-header">
                        <mat-icon class="action-icon">{{ actionIcon(action) }}</mat-icon>
                        <span class="action-title">{{ action.title }}</span>
                        <span class="action-status">{{ actionStatusLabel(action) }}</span>
                      </header>
                      <p class="action-summary">{{ action.summary }}</p>
                      @if (action.details.length) {
                        <dl class="action-details">
                          @for (detail of action.details; track $index) {
                            <div class="action-detail"><dt>{{ detail.label }}</dt><dd>{{ detail.value }}</dd></div>
                          }
                        </dl>
                      }
                      @if (isActionable(action)) {
                        <div class="action-buttons">
                          <button mat-flat-button color="primary" type="button" class="confirm-action" (click)="confirmAction(action)" [disabled]="busy || !!actionInProgress">
                            @if (actionInProgress === action.id) { <mat-spinner diameter="16" /> } @else { <mat-icon>check</mat-icon> }
                            Confirmar
                          </button>
                          <button mat-stroked-button type="button" class="cancel-action" (click)="cancelAction(action)" [disabled]="busy || !!actionInProgress">
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
                    </section>
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
                </div>
              </article>
            } @empty {
              @if (!pendingContent) {
                <div class="empty-chat">
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
    .conversation-item { display: flex; align-items: flex-start; gap: 10px; width: 100%; padding: 9px 10px; text-align: left; color: var(--text-100); background: transparent; border: 1px solid transparent; border-radius: 10px; cursor: pointer; font: inherit; }
    .conversation-item:hover:not(:disabled) { background: rgba(255,255,255,.04); }
    .conversation-item.selected { border-color: rgba(34, 197, 94, .35); background: rgba(34, 197, 94, .08); }
    .conversation-item:disabled { cursor: default; }
    .conversation-icon { width: 18px; height: 18px; font-size: 18px; margin-top: 1px; color: var(--text-300); }
    .conversation-item.selected .conversation-icon { color: var(--brand-300); }
    .conversation-body { display: grid; gap: 3px; min-width: 0; }
    .conversation-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; }
    .conversation-meta { color: var(--text-300); font-size: 11px; }
    .empty-history { margin: 4px; font-size: 13px; }

    .chat-panel { display: flex; flex-direction: column; min-width: 0; min-height: 0; padding: 0; overflow: hidden; }
    .chat-header { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 14px 18px; border-bottom: 1px solid var(--kd-border-subtle); }
    .chat-heading { min-width: 0; }
    .chat-heading h3 { margin: 0; font-size: 15px; color: var(--text-100); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .chat-heading p { margin: 3px 0 0; font-size: 12px; }
    .delete-button { color: var(--text-300); }
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

    .action-card { margin-top: 10px; padding: 12px; border-radius: 12px; border: 1px solid rgba(34, 197, 94, .35); background: rgba(34, 197, 94, .05); }
    .action-card[data-status="Confirmed"] { border-color: rgba(34, 197, 94, .25); background: rgba(34, 197, 94, .03); }
    .action-card[data-status="Cancelled"], .action-card[data-status="Expired"] { border-color: var(--kd-border-subtle); background: rgba(255,255,255,.02); opacity: .8; }
    .action-card[data-status="Failed"] { border-color: rgba(255, 99, 99, .3); background: rgba(255, 99, 99, .05); }
    .action-header { display: flex; align-items: center; gap: 8px; }
    .action-icon { color: var(--brand-300); }
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
  @ViewChild('messageList') private messageList?: ElementRef<HTMLElement>;
  private shouldScroll = false;

  readonly suggestions: Suggestion[] = [
    { icon: 'event_busy', text: '¿Qué tareas vencen en los próximos siete días?' },
    { icon: 'add_task', text: 'Agendá una tarea para mañana en el proyecto ', prefill: true },
    { icon: 'calendar_month', text: '¿Qué reuniones tengo esta semana?' },
    { icon: 'event', text: 'Agendá una reunión el jueves a las 10 con ', prefill: true },
    { icon: 'payments', text: 'Resumen de ingresos y egresos de este mes' },
    { icon: 'folder_open', text: '¿Cómo vienen los proyectos activos?' }
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

  ngOnInit(): void { this.loadConversations(); }

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

  actionIcon(action: AiPendingAction): string { return ACTION_ICONS[action.toolName] ?? 'bolt'; }

  isActionable(action: AiPendingAction): boolean {
    return action.status === 'Pending' && new Date(action.expiresAtUtc).getTime() > Date.now();
  }

  actionStatusLabel(action: AiPendingAction): string {
    const status = action.status === 'Pending' && !this.isActionable(action) ? 'Expired' : action.status;
    return ACTION_STATUS_LABELS[status] ?? status;
  }

  confirmAction(action: AiPendingAction): void { this.resolveAction(action, 'confirm'); }

  cancelAction(action: AiPendingAction): void { this.resolveAction(action, 'cancel'); }

  private resolveAction(action: AiPendingAction, kind: 'confirm' | 'cancel'): void {
    const conversation = this.activeConversation;
    if (!conversation || this.busy || this.actionInProgress) return;
    this.actionInProgress = action.id;
    this.chatError = '';
    const request = kind === 'confirm' ? this.api.confirmAction(conversation.id, action.id) : this.api.cancelAction(conversation.id, action.id);
    request.pipe(finalize(() => this.actionInProgress = null)).subscribe({
      next: result => {
        this.messages = this.messages.map(message => message.pendingActions?.some(x => x.id === result.action.id)
          ? { ...message, pendingActions: message.pendingActions!.map(x => x.id === result.action.id ? result.action : x) }
          : message);
        if (result.message) {
          this.messages = [...this.messages, result.message];
          this.activeConversation = { ...conversation, messageCount: conversation.messageCount + 1, updatedAtUtc: result.message.createdAtUtc };
          this.conversations = this.conversations.map(x => x.id === conversation.id ? this.activeConversation! : x);
          this.shouldScroll = true;
        }
      },
      error: error => { this.chatError = IntelligentOfficeService.errorMessage(error); this.reloadActive(); }
    });
  }

  onComposerKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return;
    event.preventDefault();
    this.send();
  }

  loadConversations(reset = true): void {
    this.loadingList = true;
    this.pageError = '';
    const requestedPage = reset ? 1 : this.conversationPage + 1;
    this.api.list(requestedPage).pipe(finalize(() => this.loadingList = false)).subscribe({
      next: result => {
        this.conversationPage = requestedPage;
        this.totalConversations = result.totalCount;
        this.conversations = reset ? result.items : [...this.conversations, ...result.items];
        if (reset && this.conversations.length) this.open(this.conversations[0]);
      },
      error: () => this.pageError = 'No se pudo cargar el historial de conversaciones.'
    });
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
        this.api.send(conversation.id, content, clientRequestId).pipe(finalize(() => this.busy = false)).subscribe({ next: applyResult, error: onError });
      };

      // Respuesta en vivo; si el streaming no está disponible se usa el envío común con el mismo identificador.
      this.api.sendStream(conversation.id, content, clientRequestId).subscribe({
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
