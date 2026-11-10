import { Component, EventEmitter, Input, NgZone, OnDestroy, Output, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

/** Subconjunto de la Web Speech API que se usa (Chrome y Edge la exponen como webkitSpeechRecognition). */
interface SpeechRecognitionLike {
  lang: string;
  interimResults: boolean;
  continuous: boolean;
  onresult: ((event: { resultIndex: number; results: ArrayLike<{ isFinal: boolean; 0: { transcript: string } }> }) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

type SpeechRecognitionConstructor = new () => SpeechRecognitionLike;

/** Dictado por voz en español rioplatense. Si el navegador no lo soporta, el botón no se muestra. */
@Component({
  selector: 'app-ai-voice-button',
  standalone: true,
  imports: [MatIconModule, MatTooltipModule],
  template: `
    @if (supported) {
      <button type="button" class="voice" [class.listening]="listening" [disabled]="disabled"
        [attr.aria-label]="listening ? 'Detener dictado' : 'Dictar por voz'" [attr.aria-pressed]="listening"
        [matTooltip]="listening ? 'Escuchando… tocá para terminar' : 'Dictar por voz'" (click)="toggle()">
        <mat-icon>{{ listening ? 'graphic_eq' : 'mic' }}</mat-icon>
      </button>
    }
  `,
  styles: [`
    .voice { display: grid; place-items: center; width: 40px; height: 40px; flex-shrink: 0; padding: 0; border: 1px solid var(--kd-border-subtle); border-radius: 10px;
      background: transparent; color: var(--text-300); cursor: pointer; }
    .voice:hover:not(:disabled) { color: var(--brand-300); border-color: rgba(34, 197, 94, .45); }
    .voice.listening { color: #fff; background: rgba(239, 68, 68, .75); border-color: transparent; animation: pulse 1.4s infinite; }
    .voice:disabled { opacity: .5; cursor: default; }
    @keyframes pulse { 0%, 100% { box-shadow: 0 0 0 0 rgba(239, 68, 68, .45); } 50% { box-shadow: 0 0 0 6px rgba(239, 68, 68, 0); } }
  `]
})
export class AiVoiceButtonComponent implements OnDestroy {
  private readonly zone = inject(NgZone);
  @Input() disabled = false;
  /** Texto reconocido (el dictado parcial se reemplaza a medida que se confirma). */
  @Output() readonly transcript = new EventEmitter<{ text: string; final: boolean }>();
  @Output() readonly failed = new EventEmitter<string>();

  readonly supported = !!AiVoiceButtonComponent.recognitionConstructor();
  listening = false;
  private recognition?: SpeechRecognitionLike;

  static recognitionConstructor(): SpeechRecognitionConstructor | undefined {
    const w = globalThis as unknown as { SpeechRecognition?: SpeechRecognitionConstructor; webkitSpeechRecognition?: SpeechRecognitionConstructor };
    return w.SpeechRecognition ?? w.webkitSpeechRecognition;
  }

  toggle(): void {
    if (this.listening) { this.recognition?.stop(); return; }
    const Recognition = AiVoiceButtonComponent.recognitionConstructor();
    if (!Recognition) return;
    const recognition = new Recognition();
    recognition.lang = 'es-AR';
    recognition.interimResults = true;
    recognition.continuous = false;
    recognition.onresult = event => this.zone.run(() => {
      let text = '';
      let final = false;
      for (let i = event.resultIndex; i < event.results.length; i++) {
        text += event.results[i][0].transcript;
        final = final || event.results[i].isFinal;
      }
      this.transcript.emit({ text: text.trim(), final });
    });
    recognition.onerror = event => this.zone.run(() => {
      this.listening = false;
      if (event.error !== 'aborted' && event.error !== 'no-speech')
        this.failed.emit(event.error === 'not-allowed' ? 'Habilitá el micrófono en el navegador para dictar.' : 'No se pudo usar el dictado por voz.');
    });
    recognition.onend = () => this.zone.run(() => this.listening = false);
    this.recognition = recognition;
    this.listening = true;
    recognition.start();
  }

  ngOnDestroy(): void { this.recognition?.stop(); }
}
