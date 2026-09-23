import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { AuthSessionService } from '../../../../core/auth/auth-session.service';
import { ApiResponse } from '../../models/tareas.models';

interface Attachment {
  id: string; fileName: string; contentType: string; size: number;
  createdAt: string; uploadedById: string; uploadedByName: string;
}
interface PendingFile { file: File; uploadId: string; error?: string; uploading: boolean; preview?: string; }

@Component({
  selector: 'app-tarea-attachments', standalone: true,
  imports: [MatButtonModule, DatePipe],
  templateUrl: './tarea-attachments.component.html',
  styleUrl: './tarea-attachments.component.scss'
})
export class TareaAttachmentsComponent implements OnChanges, OnDestroy {
  @Input() taskId?: string;
  @Input() canWrite = false;
  @Input() deferUploads = false;
  @Input() disabled = false;
  @Output() busyChange = new EventEmitter<boolean>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(AuthSessionService);
  readonly accept = '.png,.jpg,.jpeg,.webp,.pdf,.doc,.docx,.xls,.xlsx,.txt,.csv,.zip';
  attachments: Attachment[] = [];
  pending: PendingFile[] = [];
  previews = new Map<string, string>();
  expanded?: { preview: string; fileName: string };
  busy = false;
  loading = false;
  error = '';
  dragging = false;
  private destroyed = false;
  private readonly urls = new Set<string>();

  ngOnChanges(changes: SimpleChanges): void { if (changes['taskId'] && this.taskId) void this.load(); }
  ngOnDestroy(): void {
    this.destroyed = true;
    this.urls.forEach(url => URL.revokeObjectURL(url));
  }
  private endpoint(): string { return `/api/tasks/${this.taskId}/attachments`; }
  private message(error: any): string {
    const server = error?.error?.message ?? error?.error?.Message;
    return server === 'Ocurrió un error interno al procesar la solicitud'
      ? 'No se pudo guardar el archivo. Reintenta; si persiste, informa la hora y el nombre del archivo.'
      : server ?? 'No se pudo completar la operación. Puedes reintentar.';
  }
  private setBusy(value: boolean): void { this.busy = value; this.busyChange.emit(value); }
  private objectUrl(blob: Blob): string {
    const url = URL.createObjectURL(blob); this.urls.add(url); return url;
  }
  private releaseUrl(url?: string): void {
    if (!url) return;
    URL.revokeObjectURL(url); this.urls.delete(url);
  }
  openPreview(preview: string, fileName: string): void { this.expanded = { preview, fileName }; }

  async load(): Promise<void> {
    if (!this.taskId || this.destroyed) return;
    this.loading = true;
    try {
      const result = await firstValueFrom(this.http.get<ApiResponse<Attachment[]>>(this.endpoint()));
      if (this.destroyed) return;
      this.attachments = result.data;
      // Only raster formats accepted by the API are displayed inline.
      for (const item of this.attachments.filter(a => a.contentType.startsWith('image/'))) {
        if (this.previews.has(item.id)) continue;
        try {
          const blob = await firstValueFrom(this.http.get(`${this.endpoint()}/${item.id}`, { responseType: 'blob' }));
          if (this.destroyed) return;
          this.previews.set(item.id, this.objectUrl(blob));
        } catch { /* Download remains available if a thumbnail request fails. */ }
      }
    } catch (error) { this.error = this.message(error); }
    finally { this.loading = false; }
  }

  select(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(Array.from(input.files ?? [])); input.value = '';
  }
  drop(event: DragEvent): void {
    event.preventDefault(); event.stopPropagation(); this.dragging = false;
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }
  paste(event: ClipboardEvent): void {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length && this.canWrite && !this.disabled && !this.busy) {
      event.preventDefault(); this.addFiles(files);
    }
  }
  addFiles(files: File[]): void {
    if (!this.canWrite || this.busy || this.disabled) return;
    const errors: string[] = [];
    for (const file of files) {
      const extension = '.' + file.name.split('.').pop()?.toLowerCase();
      if (!this.accept.split(',').includes(extension)) { errors.push(`${file.name}: formato no permitido.`); continue; }
      if (!file.size || file.size > 10 * 1024 * 1024) { errors.push(`${file.name}: debe contener información y no superar 10 MB.`); continue; }
      const preview = file.type.startsWith('image/') ? this.objectUrl(file) : undefined;
      this.pending.push({ file, uploadId: crypto.randomUUID(), uploading: false, preview });
    }
    this.error = errors.join(' ');
    if (!this.deferUploads && this.taskId && this.pending.length) void this.uploadPending(this.taskId);
  }

  async uploadPending(taskId: string, only?: PendingFile): Promise<boolean> {
    this.taskId = taskId;
    if (this.busy) return false;
    this.setBusy(true);
    try {
      for (const item of only ? [only] : [...this.pending]) {
        item.uploading = true; item.error = undefined;
        const body = new FormData(); body.append('file', item.file); body.append('uploadId', item.uploadId);
        try {
          const result = await firstValueFrom(this.http.post<ApiResponse<Attachment>>(this.endpoint(), body));
          if (!this.attachments.some(a => a.id === result.data.id)) this.attachments.unshift(result.data);
          this.releaseUrl(item.preview); this.pending = this.pending.filter(p => p !== item);
        } catch (error) { item.error = this.message(error); }
        finally { item.uploading = false; }
      }
      await this.load();
      return this.pending.length === 0;
    } finally { this.setBusy(false); }
  }
  retry(item?: PendingFile): void { if (this.taskId) void this.uploadPending(this.taskId, item); }
  removePending(item: PendingFile): void { this.releaseUrl(item.preview); this.pending = this.pending.filter(p => p !== item); }
  canDelete(item: Attachment): boolean {
    return this.canWrite && (this.session.user?.permissions.includes('tasks.write') === true || item.uploadedById === this.session.user?.id);
  }
  async remove(item: Attachment): Promise<void> {
    if (this.busy || this.disabled || !this.canDelete(item) || !confirm(`¿Eliminar ${item.fileName}?`)) return;
    this.setBusy(true); this.error = '';
    try {
      await firstValueFrom(this.http.delete(this.endpoint() + '/' + item.id));
      this.attachments = this.attachments.filter(a => a.id !== item.id);
      const preview = this.previews.get(item.id);
      if (preview) { this.releaseUrl(preview); this.previews.delete(item.id); }
    } catch (error) { this.error = this.message(error); }
    finally { this.setBusy(false); }
  }
  async download(item: Attachment): Promise<void> {
    try {
      const blob = await firstValueFrom(this.http.get(this.endpoint() + '/' + item.id, { responseType: 'blob' }));
      if (this.destroyed) return;
      const url = this.objectUrl(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = item.fileName; anchor.click();
      setTimeout(() => this.releaseUrl(url), 1000);
    } catch (error) { this.error = this.message(error); }
  }
  size(bytes: number): string { return bytes < 1048576 ? `${Math.ceil(bytes / 1024)} KB` : `${(bytes / 1048576).toFixed(1)} MB`; }
}
