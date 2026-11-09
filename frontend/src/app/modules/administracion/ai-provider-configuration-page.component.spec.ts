import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AiProviderConfigurationPageComponent } from './ai-provider-configuration-page.component';

describe('Configuración del proveedor de IA', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AiProviderConfigurationPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('guarda la clave y la borra del estado de la pantalla', () => {
    const fixture = TestBed.createComponent(AiProviderConfigurationPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/provider-configuration').flush({ data: {
      isConfigured: false, provider: 'OpenAI', model: null, apiKeyHint: null,
      isConnectionVerified: false, lastConnectionTestAtUtc: null, liveDataApproved: false,
      liveDataApprovedByUserId: null, liveDataApprovedAtUtc: null, liveDataApprovalTermsVersion: null, requiredLiveDataApprovalTermsVersion: 'terms-v1',
      version: '00000000-0000-0000-0000-000000000000'
    } });

    fixture.componentInstance.model = 'model-test';
    fixture.componentInstance.apiKey = 'sk-test-secret';
    fixture.componentInstance.save();
    const save = http.expectOne('/api/ai/provider-configuration');
    expect(save.request.body.apiKey).toBe('sk-test-secret');
    save.flush({ data: {
      isConfigured: true, provider: 'OpenAI', model: 'model-test', apiKeyHint: '••••cret',
      isConnectionVerified: false, lastConnectionTestAtUtc: null, liveDataApproved: false,
      liveDataApprovedByUserId: null, liveDataApprovedAtUtc: null, liveDataApprovalTermsVersion: null,
      requiredLiveDataApprovalTermsVersion: 'terms-v1', version: 'next-version'
    } });
    fixture.detectChanges();

    expect(fixture.componentInstance.apiKey).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('sk-test-secret');
    expect(fixture.nativeElement.textContent).toContain('••••cret');
    fixture.destroy();
  });
});
