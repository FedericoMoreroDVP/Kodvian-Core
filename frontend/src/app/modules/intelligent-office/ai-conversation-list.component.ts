import { DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AiConversation } from './intelligent-office.models';

/** Lista de conversaciones de Oficina Inteligente: nueva, buscar, abrir y fijar. */
@Component({
  selector: 'app-ai-conversation-list',
  standalone: true,
  imports: [DatePipe, FormsModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule],
  template: `
    <div class="list-header">
      <button mat-flat-button color="primary" class="new-button" type="button" (click)="newConversation.emit()" [disabled]="busy"><mat-icon>add</mat-icon> Nueva conversación</button>
      <button mat-icon-button type="button" class="close-button" aria-label="Ocultar conversaciones" matTooltip="Ocultar conversaciones" (click)="closed.emit()"><mat-icon>chevron_left</mat-icon></button>
    </div>
    <input class="search" type="search" name="search" [ngModel]="search" (ngModelChange)="searchChange.emit($event)" placeholder="Buscar conversaciones…" aria-label="Buscar conversaciones" maxlength="100" />
    @if (loading) { <mat-spinner diameter="24" aria-label="Cargando conversaciones" /> }
    <div class="items">
      @for (item of conversations; track item.id) {
        <div class="row" [class.selected]="item.id === activeId">
          <button type="button" class="item" (click)="open.emit(item)" [disabled]="busy">
            <mat-icon class="icon">{{ item.isPinned ? 'push_pin' : 'chat_bubble_outline' }}</mat-icon>
            <span class="body">
              <span class="title">{{ item.title }}</span>
              <span class="meta">{{ item.updatedAtUtc | date:'dd/MM HH:mm' }} · {{ item.messageCount }} mensajes</span>
            </span>
          </button>
          <button type="button" class="pin-button" [class.pinned]="item.isPinned" [attr.aria-label]="item.isPinned ? 'Desfijar conversación' : 'Fijar conversación'"
            [matTooltip]="item.isPinned ? 'Desfijar' : 'Fijar arriba'" (click)="togglePin.emit(item)" [disabled]="busy"><mat-icon>push_pin</mat-icon></button>
        </div>
      } @empty {
        @if (!loading) { <p class="empty">{{ search.trim() ? 'No hay conversaciones que coincidan.' : 'Todavía no hay conversaciones.' }}</p> }
      }
    </div>
    @if (conversations.length < total) { <button mat-button type="button" (click)="loadMore.emit()" [disabled]="loading || busy">Cargar más</button> }
  `,
  styles: [`
    :host { display: flex; flex-direction: column; gap: 8px; min-height: 0; height: 100%; padding: 12px; box-sizing: border-box; }
    mat-icon { flex-shrink: 0; }
    .list-header { display: flex; align-items: center; gap: 4px; }
    .new-button { flex: 1; }
    .close-button { color: var(--text-300); }
    .search { width: 100%; box-sizing: border-box; padding: 7px 10px; border-radius: 8px; border: 1px solid var(--kd-border-subtle); background: rgba(0,0,0,.2); color: var(--text-100); font: inherit; font-size: 13px; }
    .items { display: grid; align-content: start; gap: 2px; flex: 1; min-height: 0; overflow-y: auto; }
    .row { display: flex; align-items: center; border: 1px solid transparent; border-radius: 10px; }
    .row.selected { border-color: rgba(34, 197, 94, .35); background: rgba(34, 197, 94, .08); }
    .item { display: flex; flex: 1; min-width: 0; align-items: flex-start; gap: 10px; padding: 8px 10px; text-align: left; color: var(--text-100); background: transparent; border: 0; border-radius: 10px; cursor: pointer; font: inherit; }
    .item:hover:not(:disabled) { background: rgba(255,255,255,.04); }
    .item:disabled { cursor: default; }
    .icon { width: 18px; height: 18px; font-size: 18px; color: var(--text-300); }
    .row.selected .icon { color: var(--brand-300); }
    .body { display: grid; gap: 2px; min-width: 0; }
    .title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; }
    .meta { color: var(--text-300); font-size: 11px; }
    .pin-button { display: grid; place-items: center; width: 28px; height: 28px; margin-right: 4px; padding: 0; border: 0; border-radius: 6px; background: transparent; color: var(--text-300); cursor: pointer; opacity: 0; }
    .row:hover .pin-button, .pin-button.pinned, .pin-button:focus-visible { opacity: 1; }
    .pin-button.pinned { color: var(--brand-300); }
    .pin-button mat-icon { width: 16px; height: 16px; font-size: 16px; }
    .empty { margin: 4px; color: var(--text-300); font-size: 13px; }
    @media (hover: none) { .pin-button { opacity: 1; } }
  `]
})
export class AiConversationListComponent {
  @Input() conversations: AiConversation[] = [];
  @Input() activeId: string | undefined;
  @Input() loading = false;
  @Input() busy = false;
  @Input() total = 0;
  @Input() search = '';
  @Output() readonly open = new EventEmitter<AiConversation>();
  @Output() readonly newConversation = new EventEmitter<void>();
  @Output() readonly togglePin = new EventEmitter<AiConversation>();
  @Output() readonly searchChange = new EventEmitter<string>();
  @Output() readonly loadMore = new EventEmitter<void>();
  @Output() readonly closed = new EventEmitter<void>();
}
