import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { AuthSessionService } from '../../../../core/auth/auth-session.service';
import { isValidGoogleDriveLink } from '../../models/project-drive-link.model';
import { ProyectoDetailDialogComponent } from './proyecto-detail-dialog.component';

describe('Documentación del proyecto: enlace de Drive', () => {
  let fixture: ComponentFixture<ProyectoDetailDialogComponent>;
  let http: HttpTestingController;
  let permissions: string[];
  const endpoint = '/api/projects/project/drive-link';
  const link = 'https://drive.google.com/drive/folders/abc?usp=sharing&resourcekey=key';

  beforeEach(() => {
    permissions = ['projects.documents.read', 'projects.documents.write'];
    TestBed.configureTestingModule({ imports: [ProyectoDetailDialogComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
      { provide: MAT_DIALOG_DATA, useValue: { id: 'project', name: 'Proyecto', isActive: true } },
      { provide: MatDialogRef, useValue: { disableClose: false } },
      { provide: MatSnackBar, useValue: jasmine.createSpyObj('snack', ['open']) },
      { provide: AuthSessionService, useValue: { ensureSessionLoaded: () => of({ permissions }) } }
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); fixture?.destroy(); });
  function open(saved: string | null): void {
    fixture = TestBed.createComponent(ProyectoDetailDialogComponent);
    fixture.detectChanges();
    http.expectOne(endpoint).flush({ data: { googleDriveFolderUrl: saved } });
    fixture.detectChanges();
  }

  it('guarda el enlace sin enviar los demás campos del proyecto y permite abrirlo', () => {
    open(null);
    fixture.componentInstance.enlaceDrive = `  ${link}  `;
    fixture.componentInstance.guardarEnlace();
    const request = http.expectOne(endpoint);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ googleDriveFolderUrl: link });
    request.flush({ data: { googleDriveFolderUrl: link } }); fixture.detectChanges();
    const anchor = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;
    expect(anchor.href).toBe(link); expect(anchor.target).toBe('_blank'); expect(anchor.rel).toContain('noopener');
    expect(fixture.nativeElement.querySelector('input[type=file]')).toBeNull();
  });

  it('conserva el texto y el enlace guardado ante un fallo', () => {
    open(link);
    const draft = 'https://drive.google.com/drive/folders/nueva';
    fixture.componentInstance.enlaceDrive = draft; fixture.componentInstance.guardarEnlace();
    http.expectOne(endpoint).flush({}, { status: 500, statusText: 'Error' }); fixture.detectChanges();
    expect(fixture.componentInstance.enlaceDrive).toBe(draft);
    expect(fixture.componentInstance.enlaceParaAbrir).toBe(link);
    expect(fixture.componentInstance.guardandoEnlace).toBeFalse();
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
  });

  it('quita el enlace y muestra el estado vacío', () => {
    open(link); fixture.componentInstance.guardarEnlace(true);
    const request = http.expectOne(endpoint); expect(request.request.body).toEqual({ googleDriveFolderUrl: null });
    request.flush({ data: { googleDriveFolderUrl: null } }); fixture.detectChanges();
    expect(fixture.componentInstance.enlaceDrive).toBe(''); expect(fixture.nativeElement.querySelector('a')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('todavía no tiene un enlace');
  });

  it('permite solo abrir a usuarios de consulta y bloquea escrituras', () => {
    permissions = ['projects.documents.read']; open(link);
    expect(fixture.nativeElement.querySelector('form')).toBeNull(); expect(fixture.nativeElement.querySelector('a')).not.toBeNull();
    fixture.componentInstance.guardarEnlace(true); http.expectNone(endpoint);
  });

  it('no consulta ni muestra enlaces sin permiso de documentación', () => {
    permissions = []; fixture = TestBed.createComponent(ProyectoDetailDialogComponent); fixture.detectChanges();
    http.expectNone(endpoint); expect(fixture.nativeElement.querySelector('a')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('No tienes permisos');
  });

  it('rechaza direcciones inválidas antes de guardar', () => {
    open(null); fixture.componentInstance.enlaceDrive = 'https://drive.google.com.ejemplo.com/folders/abc';
    fixture.componentInstance.guardarEnlace(); http.expectNone(endpoint);
    expect(fixture.componentInstance.errorEnlace).toContain('válido');
  });

  it('permite reintentar la carga fallida antes de editar', () => {
    fixture = TestBed.createComponent(ProyectoDetailDialogComponent); fixture.detectChanges();
    http.expectOne(endpoint).flush({}, { status: 500, statusText: 'Error' }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input')).toBeNull();
    fixture.componentInstance.cargarEnlace(); http.expectOne(endpoint).flush({ data: { googleDriveFolderUrl: link } });
    expect(fixture.componentInstance.enlaceDrive).toBe(link);
  });

  it('valida protocolo, dominio, credenciales y longitud conservando enlaces compartidos', () => {
    expect(isValidGoogleDriveLink(` ${link} `)).toBeTrue();
    expect(isValidGoogleDriveLink('')).toBeTrue();
    for (const invalid of ['javascript:alert(1)', 'http://drive.google.com/folder', 'https://user@drive.google.com/folder',
      'https://drive.google.com:8443/folder', 'https://drive.google.com/', 'https://drive.google.com/fo\nlder',
      'https://drive.google.com\\@evil.com/folder', 'https://drive.google.com/' + 'a'.repeat(2048)]) {
      expect(isValidGoogleDriveLink(invalid)).withContext(invalid).toBeFalse();
    }
  });
});
