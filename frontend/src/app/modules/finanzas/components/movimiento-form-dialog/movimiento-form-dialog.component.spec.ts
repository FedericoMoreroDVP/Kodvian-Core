import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { MatDialogRef } from '@angular/material/dialog';
import { MovimientoFormDialogComponent } from './movimiento-form-dialog.component';

describe('Movimiento financiero histórico', () => {
  let http: HttpTestingController;
  let ref: { close: jasmine.Spy; disableClose: boolean };
  beforeEach(() => {
    ref = { close: jasmine.createSpy(), disableClose: false };
    TestBed.configureTestingModule({ providers: [FormBuilder, provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: ref }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function create() {
    const component = TestBed.runInInjectionContext(() => new MovimientoFormDialogComponent({ tipoInicial: 'Egreso', categorias: [], clientes: [], proyectos: [], proveedores: [] }));
    http.expectOne('/api/finance/partners').flush({ data: [] });
    component.form.patchValue({ categoryId: 'category', description: 'Gasto antiguo', amount: 20, currency: 'USD', status: 'Pagado', movementDate: new Date(2025, 0, 10) });
    return component;
  }
  it('exige fecha efectiva y envía la moneda y las fechas originales', fakeAsync(() => {
    const component = create(); void component.guardar(); tick();
    http.expectNone('/api/financial-movements'); expect(component.error).toContain('fecha efectiva');
    component.form.controls.settlementDate.setValue(new Date(2025, 1, 5)); void component.guardar();
    const request = http.expectOne('/api/financial-movements'); expect(request.request.body.currency).toBe('USD');
    expect(request.request.body.movementDate).toBe('2025-01-10'); expect(request.request.body.settlementDate).toBe('2025-02-05');
    request.flush({ data: { id: 'movement', version: 'version' } }); tick(); expect(ref.close).toHaveBeenCalledWith(true);
  }));
  it('conserva los datos si falla el guardado', fakeAsync(() => {
    const component = create(); component.form.controls.settlementDate.setValue(new Date(2025, 1, 5)); void component.guardar();
    const first = http.expectOne('/api/financial-movements'); const id = first.request.body.requestId;
    first.flush({ message: 'No disponible' }, { status: 500, statusText: 'Error' }); tick();
    expect(ref.close).not.toHaveBeenCalled(); expect(component.form.controls.currency.value).toBe('USD');
    expect(component.form.controls.description.value).toBe('Gasto antiguo'); expect(component.saving).toBeFalse();
    void component.guardar(); const retry = http.expectOne('/api/financial-movements'); expect(retry.request.body.requestId).toBe(id);
    retry.flush({ data: { id, version: 'version' } }); tick();
  }));
});
