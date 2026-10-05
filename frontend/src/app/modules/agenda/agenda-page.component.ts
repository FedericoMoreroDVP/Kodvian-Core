import { Component, OnInit, inject } from '@angular/core';
import { DatePipe, DecimalPipe, NgStyle } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthSessionService } from '../../core/auth/auth-session.service';
import { ReunionFormDialogComponent } from './components/reunion-form-dialog/reunion-form-dialog.component';
import { AgendaLookupItem, EstadoReunion, PrioridadReunion, Reunion } from './models/agenda.models';
import { AgendaService } from './services/agenda.service';

type VistaAgenda = 'dia' | 'semana' | 'mes' | 'lista';
const WORKDAY_START = 0;
const WORKDAY_END = 24;
const SLOT_MINUTES = 30;
const PIXELS_PER_HOUR = 64;
@Component({ selector: 'app-agenda-page', standalone: true, imports: [DatePipe, DecimalPipe, NgStyle, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatSnackBarModule], templateUrl: './agenda-page.component.html', styleUrl: './agenda-page.component.scss' })
export class AgendaPageComponent implements OnInit {
  private readonly agenda = inject(AgendaService);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);
  private readonly snackBar = inject(MatSnackBar);
  private readonly session = inject(AuthSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly canWrite = () => this.session.user?.permissions.includes('meetings.write') ?? false;
  readonly canCancel = () => this.session.user?.permissions.includes('meetings.cancel') ?? false;
  readonly priorities: PrioridadReunion[] = ['Baja', 'Media', 'Alta', 'Urgente'];
  readonly statuses: EstadoReunion[] = ['Programada', 'Confirmada', 'Realizada', 'Cancelada'];
  readonly views: { value: VistaAgenda; label: string }[] = [{ value: 'dia', label: 'Día' }, { value: 'semana', label: 'Semana' }, { value: 'mes', label: 'Mes' }, { value: 'lista', label: 'Lista' }];
  readonly filters = this.fb.group({ projectId: [''], priority: [''], status: [''] });
  projects: AgendaLookupItem[] = [];
  users: AgendaLookupItem[] = [];
  meetings: Reunion[] = [];
  selected?: Reunion;
  loading = false;
  loadingLookups = false;
  filtersOpen = false;
  private openNewAfterLookups = false;
  view: VistaAgenda = 'mes';
  cursor = new Date();
  days: Date[] = [];
  monthDays: Date[] = [];
  get weekStart(): Date { return startOfWeek(this.cursor); }
  get rangeLabel(): string {
    if (this.view === 'dia') return formatRange(this.cursor, this.cursor);
    if (this.view === 'mes') return this.cursor.toLocaleDateString('es-AR', { month: 'long', year: 'numeric' });
    return formatRange(this.weekStart, this.days[6]);
  }
  readonly timeSlots = Array.from({ length: (WORKDAY_END - WORKDAY_START) * 2 }, (_, index) => {
    const minutesFromStart = index * SLOT_MINUTES;
    return { hour: WORKDAY_START + Math.floor(minutesFromStart / 60), minute: minutesFromStart % 60 };
  });
  get activeFilterCount(): number { return Object.values(this.filters.getRawValue()).filter(Boolean).length; }

  ngOnInit(): void {
    const projectId = this.route.snapshot.queryParamMap.get('projectId');
    if (projectId) this.filters.patchValue({ projectId });
    this.openNewAfterLookups = this.route.snapshot.queryParamMap.get('action') === 'new' && this.canWrite();
    if (this.openNewAfterLookups) this.router.navigate([], { relativeTo: this.route, queryParams: { action: null }, queryParamsHandling: 'merge', replaceUrl: true });
    this.updateDays(); this.loadLookups(); this.loadMeetings();
  }

  loadLookups(): void {
    this.loadingLookups = true;
    this.agenda.obtenerLookups().subscribe({
      next: data => {
        this.projects = data.projects; this.users = data.users; this.loadingLookups = false;
        if (this.openNewAfterLookups) { this.openNewAfterLookups = false; this.openForm(); }
      },
      error: () => { this.loadingLookups = false; this.openNewAfterLookups = false; this.snackBar.open('No se pudieron cargar los datos para crear reuniones.', 'Cerrar', { duration: 3500 }); }
    });
  }
  loadMeetings(): void {
    this.loading = true;
    const { from, to } = this.currentRange();
    const raw = this.filters.getRawValue();
    this.agenda.obtenerTodasLasReuniones({ projectId: raw.projectId || undefined, priority: raw.priority as PrioridadReunion | '', status: raw.status as EstadoReunion | '', from: from.toISOString(), to: to.toISOString() }).subscribe({
      next: meetings => { this.meetings = meetings; this.loading = false; this.selected = this.selected && meetings.find(x => x.id === this.selected!.id); },
      error: () => { this.meetings = []; this.loading = false; this.snackBar.open('No se pudo cargar la agenda.', 'Cerrar', { duration: 3500 }); }
    });
  }
  previous(): void { this.moveCursor(-1); }
  next(): void { this.moveCursor(1); }
  today(): void { this.cursor = new Date(); this.updateDays(); this.loadMeetings(); }
  applyFilters(): void { this.loadMeetings(); }
  clearFilters(): void { this.filters.reset({ projectId: '', priority: '', status: '' }); this.loadMeetings(); }
  setView(view: VistaAgenda): void { this.view = view; this.updateDays(); this.loadMeetings(); }
  meetingsFor(day: Date): Reunion[] { const start = startOfDay(day); const end = endOfDay(day); return this.meetings.filter(meeting => new Date(meeting.startsAt) <= end && new Date(meeting.endsAt) >= start); }
  isToday(day: Date): boolean { return sameDay(day, new Date()); }
  participantNames(meeting: Reunion): string { return meeting.participants.map(participant => participant.name).join(', '); }
  select(meeting: Reunion): void { this.selected = meeting; }
  closeDetail(): void { this.selected = undefined; }
  selectAndOpen(meeting: Reunion): void { this.selected = meeting; this.cursor = new Date(meeting.startsAt); this.view = 'semana'; this.updateDays(); this.loadMeetings(); }
  openForm(meeting?: Reunion, day?: Date): void {
    if (!this.canWrite() || this.loadingLookups) return;
    const dialog = this.dialog.open(ReunionFormDialogComponent, { width: '820px', maxWidth: 'calc(100vw - 24px)', maxHeight: 'calc(100vh - 24px)', data: { reunion: meeting, projects: this.projects, users: this.users, projectId: this.filters.value.projectId || undefined, startsAt: day } });
    dialog.afterClosed().subscribe(changed => { if (changed) this.loadMeetings(); });
  }
  cancelSelected(): void {
    if (!this.selected || !this.canCancel() || !confirm(`¿Cancelar "${this.selected.title}"? La reunión permanecerá en el historial.`)) return;
    this.agenda.cancelar(this.selected.id).subscribe({
      next: meeting => { this.selected = meeting; this.snackBar.open('La reunión se canceló correctamente', 'Cerrar', { duration: 3000 }); this.loadMeetings(); },
      error: error => this.snackBar.open(error?.error?.message ?? 'No se pudo cancelar la reunión.', 'Cerrar', { duration: 3500 })
    });
  }
  openDay(day: Date): void { this.cursor = new Date(day); this.view = 'dia'; this.loadMeetings(); }
  slotDate(day: Date, hour: number, minute: number): Date { const value = new Date(day); value.setHours(hour, minute, 0, 0); return value; }
  eventStyle(meeting: Reunion, day: Date): Record<string, string> {
    const dayStart = startOfDay(day);
    const dayEnd = endOfDay(day);
    const start = new Date(Math.max(new Date(meeting.startsAt).getTime(), dayStart.getTime()));
    const end = new Date(Math.min(new Date(meeting.endsAt).getTime(), dayEnd.getTime()));
    const minutesFromWorkday = ((start.getHours() * 60) + start.getMinutes()) - (WORKDAY_START * 60);
    const durationMinutes = Math.max(30, (end.getTime() - start.getTime()) / 60000);
    return {
      '--event-top': `${Math.max(0, minutesFromWorkday / 60 * PIXELS_PER_HOUR)}px`,
      '--event-height': `${Math.max(34, durationMinutes / 60 * PIXELS_PER_HOUR)}px`
    };
  }
  priorityClass(priority: PrioridadReunion): string { return `priority-${priority.toLowerCase()}`; }
  projectColor(meeting: Reunion): string { return meeting.projectCalendarColor || '#5AB0FF'; }
  private moveCursor(direction: number): void {
    const next = new Date(this.cursor);
    if (this.view === 'dia') next.setDate(next.getDate() + direction);
    else if (this.view === 'mes') next.setMonth(next.getMonth() + direction);
    else next.setDate(next.getDate() + (7 * direction));
    this.cursor = next; this.updateDays(); this.loadMeetings();
  }
  private currentRange(): { from: Date; to: Date } {
    const from = this.view === 'dia' ? startOfDay(this.cursor) : this.view === 'mes' ? firstOfMonth(this.cursor) : this.weekStart;
    const to = this.view === 'dia' ? endOfDay(this.cursor) : this.view === 'mes' ? endOfMonth(this.cursor) : endOfDay(this.days[6]);
    return { from, to };
  }
  private updateDays(): void {
    this.days = Array.from({ length: 7 }, (_, index) => addDays(this.weekStart, index));
    const gridStart = startOfWeek(firstOfMonth(this.cursor));
    this.monthDays = Array.from({ length: 42 }, (_, index) => addDays(gridStart, index));
  }
}

function startOfWeek(value: Date): Date { const date = startOfDay(value); date.setDate(date.getDate() - ((date.getDay() + 6) % 7)); return date; }
function startOfDay(value: Date): Date { const date = new Date(value); date.setHours(0, 0, 0, 0); return date; }
function endOfDay(value: Date): Date { const date = startOfDay(value); date.setHours(23, 59, 59, 999); return date; }
function firstOfMonth(value: Date): Date { const date = startOfDay(value); date.setDate(1); return date; }
function endOfMonth(value: Date): Date { const date = firstOfMonth(value); date.setMonth(date.getMonth() + 1, 0); return endOfDay(date); }
function addDays(value: Date, amount: number): Date { const date = new Date(value); date.setDate(date.getDate() + amount); return date; }
function formatRange(from: Date, to: Date): string { return from.toLocaleDateString('es-AR', { day: 'numeric', month: 'short' }) + (sameDay(from, to) ? '' : ` al ${to.toLocaleDateString('es-AR', { day: 'numeric', month: 'short', year: 'numeric' })}`); }
function sameDay(a: Date, b: Date): boolean { return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate(); }
