import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';

import { AiProviderConfigurationPageComponent } from './ai-provider-configuration-page.component';

const baseConfiguration = {
  isConfigured: false, provider: 'OpenAI', model: null, apiKeyHint: null,
  isConnectionVerified: false, lastConnectionTestAtUtc: null, liveDataApproved: false,
  liveDataApprovedByUserId: null, liveDataApprovedAtUtc: null, liveDataApprovalTermsVersion: null, requiredLiveDataApprovalTermsVersion: 'terms-v1',
  version: '00000000-0000-0000-0000-000000000000'
};

describe('Configuración del proveedor de IA', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AiProviderConfigurationPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('guarda la clave y la borra del estado de la pantalla', () => {
    const fixture = TestBed.createComponent(AiProviderConfigurationPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/provider-configuration').flush({ data: baseConfiguration });

    fixture.componentInstance.model = 'model-test';
    fixture.componentInstance.apiKey = 'sk-test-secret';
    fixture.componentInstance.save();
    const save = http.expectOne('/api/ai/provider-configuration');
    expect(save.request.body.apiKey).toBe('sk-test-secret');
    expect(save.request.body.provider).toBe('OpenAI');
    save.flush({ data: { ...baseConfiguration, isConfigured: true, model: 'model-test', apiKeyHint: '••••cret', version: 'next-version' } });
    fixture.detectChanges();

    expect(fixture.componentInstance.apiKey).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('sk-test-secret');
    expect(fixture.nativeElement.textContent).toContain('••••cret');
    fixture.destroy();
  });

  it('permite elegir Anthropic, sugiere el modelo y exige la clave del nuevo proveedor', () => {
    const fixture = TestBed.createComponent(AiProviderConfigurationPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/ai/provider-configuration').flush({ data: { ...baseConfiguration, isConfigured: true, model: 'model-test', apiKeyHint: '••••cret', version: 'v1' } });
    const component = fixture.componentInstance;
    expect(component.keyRequired).toBeFalse();

    component.provider = 'Anthropic';
    component.onProviderChange();
    fixture.detectChanges();

    expect(component.providerChanged).toBeTrue();
    expect(component.keyRequired).toBeTrue();
    expect(component.hasUnsavedChanges).toBeTrue();
    const testButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(b => b.textContent?.includes('Probar conexión'))!;
    expect(testButton.disabled).toBeTrue();
    component.model = 'wrkspc_0123';
    expect(component.looksLikeWorkspaceId).toBeTrue();
    component.model = 'model-test';
    expect(component.model).toBe('model-test');
    expect(fixture.nativeElement.textContent).toContain('no genera texto ni costo');

    component.model = '';
    component.onProviderChange();
    expect(component.model).toBe('claude-opus-5-5');

    component.apiKey = 'sk-ant-test';
    component.save();
    const save = http.expectOne('/api/ai/provider-configuration');
    expect(save.request.body.provider).toBe('Anthropic');
    expect(save.request.body.model).toBe('claude-opus-5-5');
    save.flush({ data: { ...baseConfiguration, isConfigured: true, provider: 'Anthropic', model: 'claude-opus-5-5', apiKeyHint: '••••test', version: 'v2' } });
    fixture.detectChanges();

    expect(component.providerChanged).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('se envían a Anthropic');
    fixture.destroy();
  });
});
