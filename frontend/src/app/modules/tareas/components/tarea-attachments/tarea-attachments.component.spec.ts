import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { AuthSessionService } from '../../../../core/auth/auth-session.service';
import { TareaAttachmentsComponent } from './tarea-attachments.component';

describe('Adjuntos de tarea', () => {
  let component: TareaAttachmentsComponent;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthSessionService, useValue: { user: { id: 'author', permissions: [] } } }
    ] });
    component = TestBed.runInInjectionContext(() => new TareaAttachmentsComponent());
    component.canWrite = true; component.deferUploads = true;
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); component.ngOnDestroy(); });

  it('rechaza tipos inválidos y archivos vacíos o de más de 10 MB antes de subir', () => {
    component.addFiles([new File(['x'], 'mal.exe'), new File([], 'vacio.txt'),
      new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'grande.zip')]);
    expect(component.pending.length).toBe(0);
    expect(component.error).toContain('formato no permitido');
    expect(component.error).toContain('10 MB');
  });
  it('en creación mantiene los archivos pendientes hasta que la tarea exista', () => {
    component.addFiles([new File(['texto'], 'prueba.txt')]);
    expect(component.pending.length).toBe(1);
    http.expectNone(() => true);
  });
  it('reintenta con el mismo identificador y no vuelve a subir archivos exitosos', fakeAsync(() => {
    component.addFiles([new File(['a'], 'a.txt'), new File(['b'], 'b.txt')]);
    const retryId = component.pending[1].uploadId;
    let completed = true;
    void component.uploadPending('task').then(ok => completed = ok);
    const first = http.expectOne(r => r.method === 'POST');
    first.flush({ data: { id: 'a', fileName: 'a.txt', contentType: 'text/plain' } }); tick();
    const second = http.expectOne(r => r.method === 'POST');
    second.flush({ message: 'Falló la subida' }, { status: 500, statusText: 'Error' }); tick();
    http.expectOne(r => r.method === 'GET').flush({ data: [{ id: 'a', fileName: 'a.txt', contentType: 'text/plain' }] }); tick();
    expect(completed).toBeFalse(); expect(component.pending.length).toBe(1);
    void component.uploadPending('task').then(ok => completed = ok);
    const retry = http.expectOne(r => r.method === 'POST');
    expect((retry.request.body as FormData).get('uploadId')).toBe(retryId);
    retry.flush({ data: { id: 'b', fileName: 'b.txt', contentType: 'text/plain' } }); tick();
    http.expectOne(r => r.method === 'GET').flush({ data: [] }); tick();
    expect(completed).toBeTrue(); expect(component.pending.length).toBe(0);
  }));
  it('admite capturas pegadas y archivos arrastrados', () => {
    const file = new File(['image'], 'captura.png', { type: 'image/png' });
    const preventDefault = jasmine.createSpy();
    component.paste({ clipboardData: { files: [file] }, preventDefault } as unknown as ClipboardEvent);
    component.drop({ dataTransfer: { files: [file] }, preventDefault, stopPropagation: () => {} } as unknown as DragEvent);
    expect(component.pending.length).toBe(2); expect(preventDefault).toHaveBeenCalled();
  });
  it('bloquea la carga para usuarios de lectura', () => {
    component.canWrite = false;
    component.addFiles([new File(['texto'], 'prueba.txt')]);
    expect(component.pending.length).toBe(0);
  });
});
