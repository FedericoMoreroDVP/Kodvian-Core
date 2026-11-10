import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { AiPreferencesComponent } from './ai-preferences.component';

describe('Preferencias del asistente', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AiPreferencesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('carga las preferencias y guarda los cambios', () => {
    const fixture = TestBed.createComponent(AiPreferencesComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/preferences').flush({ data: { preferredCurrency: 'ARS', defaultMeetingMinutes: null, defaultTaskPriority: null, responseStyle: null, defaultScope: null, notes: null } });
    expect(fixture.componentInstance.model.preferredCurrency).toBe('ARS');

    fixture.componentInstance.model.defaultMeetingMinutes = '30' as unknown as number;
    fixture.componentInstance.model.responseStyle = 'breve';
    fixture.componentInstance.save();
    const put = http.expectOne('/api/ai/preferences');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.defaultMeetingMinutes).toBe(30);
    put.flush({ data: { ...put.request.body } });

    expect(fixture.componentInstance.saved).toBeTrue();
  });
});
