import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { MatDialogRef } from '@angular/material/dialog';
import { PagoDesarrolladorFormDialogComponent } from './pago-desarrollador-form-dialog.component';
import { PagoDesarrollador } from '../../models/proyectos.models';

describe('Pago y egreso financiero vinculado', () => {
  let http: HttpTestingController;
  let ref: { close: jasmine.Spy; disableClose: boolean };
  beforeEach(() => {
    ref = { close: jasmine.createSpy(), disableClose: false };
    TestBed.configureTestingModule({ providers: [FormBuilder, provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: ref }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('envía ambas monedas e importes y mantiene el identificador al reintentar', fakeAsync(() => {
    const component = TestBed.runInInjectionContext(() => new PagoDesarrolladorFormDialogComponent({ contractId: 'contract', currency: 'USD' }));
    component.form.patchValue({ amount: 450000, currency: 'ARS', appliedCurrency: 'USD', appliedAmount: 300 });
    void component.guardar(); const request = http.expectOne('/api/developer-contracts/contract/payments'); const operationId = request.request.body.requestId;
    expect(request.request.body.amount).toBe(450000); expect(request.request.body.appliedAmount).toBe(300); expect(request.request.body.currency).toBe('ARS');
    expect(ref.disableClose).toBeTrue(); request.flush({}, { status: 500, statusText: 'Error' }); tick();
    expect(ref.close).not.toHaveBeenCalled(); expect(component.form.controls.amount.value).toBe(450000);
    void component.guardar(); const retry = http.expectOne('/api/developer-contracts/contract/payments'); expect(retry.request.body.requestId).toBe(operationId);
    retry.flush({ data: { id: 'saved', financialMovementId: 'expense', version: 'v1' } }); tick();
    expect(ref.close).toHaveBeenCalledWith(true); component.ngOnDestroy();
  }));
  it('no asigna moneda arbitraria a un pago histórico y exige vincular un egreso', fakeAsync(() => {
    const historical = { id: 'old', amount: 400, paymentDate: '2025-02-10', periodYear: 2025, periodMonth: 2, version: 'v1', isActive: true } as PagoDesarrollador;
    const component = TestBed.runInInjectionContext(() => new PagoDesarrolladorFormDialogComponent({ contractId: 'contract', payment: historical }));
    expect(component.form.controls.currency.value).toBe(''); expect(component.linkExisting).toBeTrue();
    component.form.patchValue({ currency: 'ARS', appliedCurrency: 'ARS', appliedAmount: 400 }); void component.guardar(); tick();
    http.expectNone('/api/developer-payments/old'); expect(component.error).toContain('egreso existente');
    component.form.controls.existingMovementId.setValue('existing'); void component.guardar();
    const request = http.expectOne('/api/developer-payments/old'); expect(request.request.body.existingMovementId).toBe('existing'); expect(request.request.body.expectedVersion).toBe('v1');
    request.flush({ data: { ...historical, financialMovementId: 'existing', version: 'v2' } }); tick(); component.ngOnDestroy();
  }));
  it('iguala automáticamente el importe cancelado cuando se paga en la misma moneda', () => {
    const component = TestBed.runInInjectionContext(() => new PagoDesarrolladorFormDialogComponent({ contractId: 'contract', currency: 'ARS' }));
    component.form.controls.amount.setValue(250);
    expect(component.form.controls.appliedAmount.value).toBe(250); component.ngOnDestroy();
  });
});
