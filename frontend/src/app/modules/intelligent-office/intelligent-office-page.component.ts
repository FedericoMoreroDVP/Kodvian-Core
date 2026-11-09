import { DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { AiConversation, AiConversationDetail, AiMessage } from './intelligent-office.models';
import { IntelligentOfficeService } from './intelligent-office.service';

@Component({
  selector: 'app-intelligent-office-page',
  standalone: true,
  imports: [DatePipe, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, RouterLink],
  template: `
    <section class="office-page">
      <mat-card class="hero-card">
        <div>
          <p class="eyebrow">Kodvian Core</p>
          <h2>Oficina Inteligente</h2>
          <p>Consultas operativas con fuentes verificables. En esta etapa la IA solo puede leer información autorizada.</p>
        </div>
        <a mat-button routerLink="/administracion/configuracion-ia">Configuración del proveedor</a>
      </mat-card>

      @if (pageError) { <p class="error" role="alert">{{ pageError }}</p> }
      @if (pageMessage) { <p class="notice" role="status">{{ pageMessage }}</p> }

      <div class="workspace">
        <mat-card class="history-panel">
          <div class="history-header"><h3>Conversaciones</h3><button mat-icon-button type="button" aria-label="Nueva conversación" (click)="newConversation()" [disabled]="busy"><mat-icon>add</mat-icon></button></div>
          @if (loadingList) { <mat-spinner diameter="28" aria-label="Cargando conversaciones" /> }
          <div class="conversation-list">
            @for (item of conversations; track item.id) {
              <button type="button" class="conversation-item" [class.selected]="item.id === activeConversation?.id" (click)="open(item)" [disabled]="busy">
                <span class="conversation-title">{{ item.title }}</span>
                <span class="conversation-meta">{{ item.updatedAtUtc | date:'short' }} · {{ item.messageCount }} mensajes</span>
              </button>
            } @empty { @if (!loadingList) { <p class="muted">Todavía no hay conversaciones.</p> } }
          </div>
          @if (conversations.length < totalConversations) { <button mat-button type="button" (click)="loadMoreConversations()" [disabled]="loadingList || busy">Cargar más conversaciones</button> }
          @if (activeConversation) {
            <button mat-button color="warn" type="button" (click)="deleteConversation()" [disabled]="busy">Eliminar conversación</button>
          }
        </mat-card>

        <mat-card class="chat-panel">
          <div class="chat-header">
            <div><h3>{{ activeConversation?.title ?? 'Nueva conversación' }}</h3><p class="muted">Las conversaciones se conservan hasta que las elimines.</p></div>
            @if (busy) { <mat-spinner diameter="26" aria-label="Procesando consulta" /> }
          </div>

          @if (chatError) { <p class="error" role="alert">{{ chatError }} @if (hasPendingRequest) { <button mat-button type="button" (click)="retryAsNewRun()" [disabled]="busy">Iniciar un nuevo intento</button> }</p> }
          <div class="messages" aria-live="polite">
            @if (activeConversation && messages.length < activeConversation.messageCount) { <p class="muted">Se muestran los últimos 200 mensajes de {{ activeConversation.messageCount }}.</p> }
            @for (message of messages; track message.id) {
              <article class="message" [class.user-message]="message.role === 'user'" [class.assistant-message]="message.role === 'assistant'">
                <div class="message-label"><span>{{ message.role === 'user' ? 'Vos' : 'Oficina Inteligente' }} @if (message.traceId) { <small>Seguimiento {{ message.traceId }}</small> }</span><time>{{ message.createdAtUtc | date:'short' }}</time></div>
                <p class="message-content">{{ message.content }}</p>
                @if (message.sources.length) {
                  <div class="sources">
                    <strong>Fuentes</strong>
                    @for (source of message.sources; track $index) {
                      <span class="source-item">
                        @if (source.link) { <a [href]="source.link">{{ source.toolName }}</a> } @else { {{ source.toolName }} }
                        <span>{{ source.from && source.to ? (source.from + ' a ' + source.to) : '' }}</span>
                        @if (source.currency) { <span>{{ source.currency }}</span> }
                        <time>{{ source.asOfUtc | date:'short' }}</time>
                      </span>
                    }
                  </div>
                }
              </article>
            } @empty {
              <div class="empty-chat"><mat-icon>auto_awesome</mat-icon><p>Preguntá por tareas, proyectos, reuniones o finanzas autorizadas.</p><p class="muted">Ejemplo: “¿Qué tareas vencen en los próximos siete días?”</p></div>
            }
          </div>

          <form class="composer" (ngSubmit)="send()">
            <mat-form-field appearance="outline">
              <mat-label>Escribí tu consulta</mat-label>
              <textarea matInput name="message" [(ngModel)]="draft" maxlength="4000" rows="3" [disabled]="busy" placeholder="Consultas de solo lectura"></textarea>
              <mat-hint align="end">{{ draft.length }} / 4000</mat-hint>
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="busy || !draft.trim()"><mat-icon>send</mat-icon> Consultar</button>
          </form>
        </mat-card>
      </div>
    </section>
  `,
  styles: [`
    .office-page { display: grid; gap: 12px; min-width: 0; }
    .hero-card, .history-panel, .chat-panel { border: 1px solid var(--kd-border-subtle); border-radius: 16px; background: linear-gradient(180deg, rgba(17,26,36,.94), rgba(13,21,30,.98)); color: var(--text-100); }
    .hero-card { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 18px; }
    .eyebrow { margin: 0 0 5px; color: var(--brand-300); font-size: 11px; letter-spacing: 1.2px; text-transform: uppercase; }
    h2 { margin: 0; color: var(--brand-300); font-size: 30px; }
    h3 { margin: 0; font-size: 16px; color: var(--kd-text-secondary); }
    .hero-card p:last-child, .muted { color: var(--text-300); }
    .workspace { display: grid; grid-template-columns: minmax(220px, 290px) minmax(0, 1fr); gap: 12px; min-height: 65vh; }
    .history-panel { display: flex; flex-direction: column; gap: 10px; padding: 12px; }
    .history-header, .chat-header { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
    .conversation-list { display: grid; align-content: start; gap: 6px; overflow-y: auto; }
    .conversation-item { display: grid; gap: 5px; width: 100%; padding: 10px; text-align: left; color: var(--text-100); background: transparent; border: 1px solid transparent; border-radius: 10px; cursor: pointer; }
    .conversation-item:hover, .conversation-item.selected { border-color: var(--kd-border-subtle); background: rgba(255,255,255,.04); }
    .conversation-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .conversation-meta { color: var(--text-300); font-size: 11px; }
    .chat-panel { display: flex; flex-direction: column; min-width: 0; padding: 16px; }
    .chat-header { padding-bottom: 10px; border-bottom: 1px solid var(--kd-border-subtle); }
    .chat-header p { margin: 5px 0 0; font-size: 12px; }
    .messages { display: flex; flex: 1; flex-direction: column; gap: 12px; min-height: 300px; max-height: 58vh; padding: 14px 2px; overflow-y: auto; }
    .message { max-width: min(900px, 92%); padding: 12px 14px; border: 1px solid var(--kd-border-subtle); border-radius: 12px; overflow-wrap: anywhere; }
    .user-message { align-self: flex-end; background: rgba(50, 94, 80, .28); }
    .assistant-message { align-self: flex-start; background: rgba(255,255,255,.025); }
    .message-label { display: flex; justify-content: space-between; gap: 12px; color: var(--kd-text-secondary); font-size: 12px; font-weight: 600; }
    time, .source-item { color: var(--text-300); font-size: 11px; }
    .message-content { margin: 8px 0 0; white-space: pre-wrap; line-height: 1.5; }
    .sources { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 12px; padding-top: 9px; border-top: 1px solid var(--kd-border-subtle); }
    .source-item { display: inline-flex; flex-wrap: wrap; gap: 6px; align-items: center; padding: 4px 8px; border-radius: 8px; background: rgba(255,255,255,.04); }
    .source-item a { color: var(--brand-300); }
    .empty-chat { margin: auto; text-align: center; color: var(--text-100); }
    .empty-chat mat-icon { color: var(--brand-300); }
    .composer { display: flex; align-items: flex-end; gap: 12px; border-top: 1px solid var(--kd-border-subtle); padding-top: 12px; }
    .composer mat-form-field { flex: 1; }
    .error { color: #ff9b9b; }
    .notice { color: var(--brand-300); }
    @media (max-width: 760px) {
      .hero-card { align-items: flex-start; flex-direction: column; }
      .workspace { grid-template-columns: 1fr; }
      .history-panel { max-height: 220px; }
      .conversation-list { max-height: 140px; }
      .messages { max-height: 52vh; }
      .composer { align-items: stretch; flex-direction: column; }
      .composer button { align-self: flex-end; }
    }
  `]
})
export class IntelligentOfficePageComponent implements OnInit {
  private readonly api = inject(IntelligentOfficeService);
  conversations: AiConversation[] = [];
  activeConversation?: AiConversation;
  messages: AiMessage[] = [];
  draft = '';
  loadingList = true;
  totalConversations = 0;
  private conversationPage = 1;
  busy = false;
  pageError = '';
  pageMessage = '';
  chatError = '';
  private pendingRequest: { content: string; id: string } | null = null;

  ngOnInit(): void { this.loadConversations(); }

  get hasPendingRequest(): boolean { return this.pendingRequest !== null; }

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
    const submit = (conversation: AiConversation): void => {
      this.activeConversation = conversation;
      if (!this.pendingRequest || this.pendingRequest.content !== content)
        this.pendingRequest = { content, id: globalThis.crypto?.randomUUID?.() ?? 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, character => {
          const random = Math.floor(Math.random() * 16);
          return (character === 'x' ? random : (random & 0x3) | 0x8).toString(16);
        }) };
      const clientRequestId = this.pendingRequest.id;
      this.api.send(conversation.id, content, clientRequestId).pipe(finalize(() => this.busy = false)).subscribe({
        next: result => {
          this.pendingRequest = null;
          const userWasLoaded = this.messages.some(x => x.id === result.userMessage.id);
          const assistantWasLoaded = this.messages.some(x => x.id === result.assistantMessage.id);
          this.messages = [...this.messages.filter(x => x.id !== result.userMessage.id && x.id !== result.assistantMessage.id), result.userMessage, result.assistantMessage];
          this.draft = '';
          this.conversations = this.conversations.map(x => x.id === conversation.id
            ? { ...x, title: x.title === 'Nueva conversación' ? content.slice(0, 120) : x.title, updatedAtUtc: result.assistantMessage.createdAtUtc, messageCount: x.messageCount + (userWasLoaded ? 0 : 1) + (assistantWasLoaded ? 0 : 1) }
            : x);
          this.activeConversation = this.conversations.find(x => x.id === conversation.id) ?? this.activeConversation;
        },
        error: error => { this.chatError = IntelligentOfficeService.errorMessage(error); this.reloadActive(); }
      });
    };

    if (this.activeConversation) submit(this.activeConversation);
    else this.api.create().subscribe({
      next: conversation => { this.conversations = [conversation, ...this.conversations]; submit(conversation); },
      error: () => { this.busy = false; this.chatError = 'No se pudo iniciar la conversación.'; }
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
      next: detail => this.messages = detail.messages,
      error: () => undefined
    });
  }
}
