import { TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import { TareasService } from '../../services/tareas.service';
import { TareaFormDialogComponent } from './tarea-form-dialog.component';
import { TareaAttachmentsComponent } from '../tarea-attachments/tarea-attachments.component';

describe('Guardado de tarea y evidencias', () => {
  let component: TareaFormDialogComponent;
  let api: jasmine.SpyObj<TareasService>;
  let dialog: jasmine.SpyObj<MatDialogRef<TareaFormDialogComponent>>;
  let attachments: jasmine.SpyObj<TareaAttachmentsComponent>;
  beforeEach(() => {
    api = jasmine.createSpyObj('api', ['crear', 'actualizar']);
    dialog = jasmine.createSpyObj('dialog', ['close']);
    attachments = jasmine.createSpyObj('adjuntos', ['uploadPending']);
    TestBed.configureTestingModule({ providers: [FormBuilder,
      { provide: TareasService, useValue: api }, { provide: MatDialogRef, useValue: dialog }
    ] });
    component = TestBed.runInInjectionContext(() => new TareaFormDialogComponent({ projects: [], developers: [] }));
    component.attachments = attachments;
    component.form.patchValue({ projectId: 'project', title: 'Mejorar Planner' });
  });
  it('conserva el formulario abierto y los datos si falla el guardado', async () => {
    api.crear.and.returnValue(throwError(() => new Error('red')));
    await component.guardar();
    expect(dialog.close).not.toHaveBeenCalled();
    expect(component.form.value.title).toBe('Mejorar Planner');
    expect(component.saving).toBeFalse();
    expect(attachments.uploadPending).not.toHaveBeenCalled();
  });
  it('no duplica la tarea al reintentar adjuntos que fallaron', async () => {
    api.crear.and.returnValue(of({ id: 'created' } as any));
    api.actualizar.and.returnValue(of({ id: 'created' } as any));
    attachments.uploadPending.and.returnValues(Promise.resolve(false), Promise.resolve(true));
    await component.guardar();
    expect(component.savedTaskId).toBe('created'); expect(dialog.close).not.toHaveBeenCalled();
    await component.guardar();
    expect(api.crear).toHaveBeenCalledTimes(1);
    expect(api.actualizar).toHaveBeenCalledTimes(1);
    expect(dialog.close).toHaveBeenCalledWith(true);
  });
});
