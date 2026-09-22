import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MovimientoFormDialogComponent } from './movimiento-form-dialog.component';
import { MovimientoDetalle } from '../../models/finanzas.models';

describe('Movimiento financiero histórico', () => {
  let http: HttpTestingController;
  let ref: { close: jasmine.Spy; disableClose: boolean };
  beforeEach(() => {
    ref = { close: jasmine.createSpy(), disableClose: false };
    TestBed.configureTestingModule({ providers: [FormBuilder, provideHttpClient(), provideHttpClientTesting(), provideNativeDateAdapter(), provideNoopAnimations(),
      { provide: MatDialogRef, useValue: ref }, { provide: MAT_DIALOG_DATA, useValue: { tipoInicial: 'Egreso', categorias: [], clientes: [], proyectos: [], proveedores: [] } }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function create() {
    const component = TestBed.runInInjectionContext(() => new MovimientoFormDialogComponent({ tipoInicial: 'Egreso', categorias: [{ id: 'category', name: 'Gastos', movementType: 'Egreso', isActive: true }], clientes: [], proyectos: [], proveedores: [] }));
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
  it('excluye aportes de dinero en egresos y rechaza la combinación original', fakeAsync(() => {
    const component = create();
    expect(component.natures.map(x => x.value)).toEqual(['Operacion', 'RetiroSocio', 'ReintegroSocio']);
    component.form.patchValue({ nature: 'AporteSocio', partnerId: 'partner', settlementDate: new Date(2025, 1, 5) });
    void component.guardar(); tick();
    http.expectNone('/api/financial-movements'); expect(component.error).toContain('Quién afrontó el gasto');
  }));
  it('limpia categoría y clasificación incompatibles al cambiar tipo y adapta el estado', () => {
    const component = create();
    component.form.patchValue({ nature: 'RetiroSocio', partnerId: 'partner', movementType: 'Ingreso' });
    component.onMovementTypeChange();
    expect(component.natures.map(x => x.value)).toEqual(['Operacion', 'AporteSocio']);
    expect(component.form.getRawValue()).toEqual(jasmine.objectContaining({ nature: 'Operacion', categoryId: '', status: 'Cobrado', partnerId: '' }));
  });
  it('explica categoría, estado, socio y fecha incompatibles antes de enviar', fakeAsync(() => {
    const component = create();
    component.form.controls.categoryId.setValue('unknown'); void component.guardar(); expect(component.error).toContain('categoría compatible');
    component.form.patchValue({ categoryId: 'category', funding: 'SocioAporte', status: 'Pendiente' });
    void component.guardar(); expect(component.error).toContain('Pagado');
    component.form.controls.status.setValue('Pagado'); void component.guardar(); expect(component.error).toContain('fecha efectiva');
    component.form.controls.settlementDate.setValue(new Date(2025, 1, 5)); void component.guardar(); expect(component.error).toContain('Selecciona el socio');
    component.form.controls.categoryId.setValue(''); void component.guardar(); expect(component.error).toContain('Categoría: completa');
    tick(); http.expectNone('/api/financial-movements');
  }));
  it('actualiza el mismo egreso de ARS 400000 como aporte sin cambiar fechas ni versión esperada', fakeAsync(() => {
    const movement: MovimientoDetalle = { id: 'marketing', version: 'original-version', movementType: 'Egreso', categoryId: 'category', categoryName: 'Marketing',
      description: 'Pago Marketing', amount: 400000, currency: 'ARS', nature: 'Operacion', funding: 'Empresa', status: 'Pagado',
      movementDate: '2025-09-01', dueDate: '2025-09-01', settlementDate: '2025-09-01', settlementDateEstimated: true,
      paymentMethod: 'Mercado Pago', receiptNumber: '176693693860', createdById: 'user', createdByName: 'Administrador', createdAt: '2025-09-01' };
    const component = TestBed.runInInjectionContext(() => new MovimientoFormDialogComponent({ tipoInicial: 'Egreso', movimiento: movement, categorias: [], clientes: [], proyectos: [], proveedores: [] }));
    http.expectOne('/api/finance/partners').flush({ data: [{ id: 'partner', fullName: 'Socio', isActive: true }] });
    component.form.patchValue({ funding: 'SocioAporte', partnerId: 'partner' }); void component.guardar();
    const request = http.expectOne('/api/financial-movements/marketing');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(jasmine.objectContaining({ movementType: 'Egreso', nature: 'Operacion', funding: 'SocioAporte', partnerId: 'partner',
      amount: 400000, currency: 'ARS', movementDate: movement.movementDate, dueDate: movement.dueDate, settlementDate: movement.settlementDate,
      settlementDateEstimated: true, expectedVersion: movement.version, receiptNumber: movement.receiptNumber, paymentMethod: movement.paymentMethod }));
    http.expectNone('/api/financial-movements'); request.flush({ data: { ...movement, funding: 'SocioAporte', partnerId: 'partner', version: 'new-version' } });
    tick(); expect(ref.close).toHaveBeenCalledWith(true);
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
  it('muestra el error junto a Guardar y enfoca el campo inválido', fakeAsync(() => {
    const fixture = TestBed.createComponent(MovimientoFormDialogComponent);
    http.expectOne('/api/finance/partners').flush({ data: [] });
    fixture.detectChanges(); tick();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector<HTMLButtonElement>('.action-save')!.click(); fixture.detectChanges(); tick();
    const feedback = host.querySelector('mat-dialog-actions [role="alert"]');
    expect(feedback?.textContent).toContain('Categoría: completa');
    expect(document.activeElement).toBe(host.querySelector('[formcontrolname="categoryId"]'));
    expect(host.querySelector('mat-dialog-content [role="alert"]')).toBeNull();
    fixture.destroy();
  }));
});
