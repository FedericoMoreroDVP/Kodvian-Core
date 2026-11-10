import { AsyncPipe } from '@angular/common';
import { Component, ElementRef, HostListener, ViewChild, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs';

import { AuthSessionService } from '../core/auth/auth-session.service';

/**
 * Acceso rápido a Oficina Inteligente desde cualquier pantalla: botón flotante o Ctrl+K (Cmd+K en Mac).
 * La pregunta se abre en Oficina Inteligente junto con la pantalla de origen como contexto.
 */
@Component({
  selector: 'app-ai-quick-launcher',
  standalone: true,
  imports: [AsyncPipe, FormsModule, MatIconModule, MatTooltipModule],
  template: `
    @if ((canUse$ | async) && !onAssistantPage()) {
      @if (open) {
        <div class="backdrop" (click)="close()"></div>
        <form class="launcher" role="dialog" aria-label="Preguntarle a Oficina Inteligente" (ngSubmit)="submit()">
          <mat-icon class="spark">auto_awesome</mat-icon>
          <input #input name="question" [(ngModel)]="question" maxlength="4000" autocomplete="off"
            placeholder="Preguntale algo a Oficina Inteligente… (Enter para enviar)" aria-label="Pregunta para Oficina Inteligente" (keydown.escape)="close()" />
          <span class="context">desde {{ currentPath() }}</span>
        </form>
      } @else {
        <button type="button" class="fab" aria-label="Abrir Oficina Inteligente (Ctrl+K)" matTooltip="Preguntale a la IA (Ctrl+K)" matTooltipPosition="left" (click)="show()">
          <mat-icon>auto_awesome</mat-icon>
        </button>
      }
    }
  `,
  styles: [`
    .fab { position: fixed; right: 22px; bottom: 22px; z-index: 900; display: grid; place-items: center; width: 52px; height: 52px; border: 0; border-radius: 50%;
      color: #04130a; background: var(--brand-300, #4ade80); box-shadow: 0 10px 28px rgba(0,0,0,.45); cursor: pointer; transition: transform .15s; }
    .fab:hover { transform: scale(1.06); }
    .backdrop { position: fixed; inset: 0; z-index: 950; background: rgba(4, 8, 12, .55); }
    .launcher { position: fixed; z-index: 960; top: 18vh; left: 50%; transform: translateX(-50%); width: min(640px, calc(100vw - 32px)); display: flex; align-items: center; gap: 10px;
      padding: 12px 14px; border-radius: 14px; border: 1px solid rgba(34, 197, 94, .45); background: #111a24; box-shadow: 0 24px 60px rgba(0,0,0,.55); }
    .spark { color: var(--brand-300, #4ade80); flex-shrink: 0; }
    input { flex: 1; min-width: 0; border: 0; outline: 0; background: transparent; color: var(--text-100, #f1f5f9); font: inherit; font-size: 15px; }
    .context { flex-shrink: 0; color: var(--text-300, #94a3b8); font-size: 11px; }
    @media (max-width: 600px) { .context { display: none; } .fab { right: 16px; bottom: 16px; } }
  `]
})
export class AiQuickLauncherComponent {
  private readonly router = inject(Router);
  private readonly session = inject(AuthSessionService);
  @ViewChild('input') private input?: ElementRef<HTMLInputElement>;

  readonly canUse$ = this.session.hasPermission('ai.office.use');
  private readonly url = toSignal(this.router.events.pipe(
    filter((event): event is NavigationEnd => event instanceof NavigationEnd),
    map(event => event.urlAfterRedirects),
    startWith(this.router.url)
  ), { initialValue: this.router.url });

  open = false;
  question = '';

  currentPath(): string { return (this.url() ?? '/').split(/[?#]/)[0] || '/'; }

  onAssistantPage(): boolean { return this.currentPath().startsWith('/oficina-inteligente'); }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (this.onAssistantPage()) return;
      if (this.open) this.close(); else this.show();
    }
  }

  show(): void {
    this.open = true;
    setTimeout(() => this.input?.nativeElement.focus());
  }

  close(): void {
    this.open = false;
    this.question = '';
  }

  submit(): void {
    const question = this.question.trim();
    if (!question) return;
    const ctx = this.currentPath();
    this.close();
    void this.router.navigate(['/oficina-inteligente'], { queryParams: { q: question, ctx } });
  }
}
