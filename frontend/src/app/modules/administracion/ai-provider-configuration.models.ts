export interface AiProviderConfiguration {
  isConfigured: boolean;
  provider: string;
  model: string | null;
  apiKeyHint: string | null;
  isConnectionVerified: boolean;
  lastConnectionTestAtUtc: string | null;
  liveDataApproved: boolean;
  liveDataApprovedByUserId: string | null;
  liveDataApprovedAtUtc: string | null;
  liveDataApprovalTermsVersion: string | null;
  requiredLiveDataApprovalTermsVersion: string;
  version: string;
}

export interface AiProviderConnectionTest {
  isSuccessful: boolean;
  message: string;
  testedAtUtc: string | null;
}

export type AiProviderId = 'OpenAI' | 'Anthropic';

export interface AiProviderOption {
  id: AiProviderId;
  label: string;
  company: string;
  defaultModel: string;
  modelPlaceholder: string;
  modelHint: string;
  keyHint: string;
  testNotice: string;
  privacyNotice: string;
}

export const AI_PROVIDERS: AiProviderOption[] = [
  {
    id: 'OpenAI',
    label: 'OpenAI',
    company: 'OpenAI',
    defaultModel: '',
    modelPlaceholder: 'Modelo habilitado en tu proyecto de OpenAI',
    modelHint: 'Usa un modelo habilitado para la clave API de tu proyecto de OpenAI.',
    keyHint: 'Clave creada en platform.openai.com → API keys (empieza con sk-).',
    testNotice: 'La prueba envía una solicitud mínima a OpenAI con el modelo seleccionado y puede generar un costo pequeño en la cuenta de API.',
    privacyNotice: 'La consulta y los datos operativos mínimos necesarios se envían a OpenAI. La aplicación solicita que no se almacene el estado de las respuestas, pero pueden aplicar los controles de seguridad y retención del proveedor. Revisa sus condiciones y configura un límite de gasto en el proyecto API antes de habilitar este uso.'
  },
  {
    id: 'Anthropic',
    label: 'Anthropic (Claude)',
    company: 'Anthropic',
    defaultModel: 'claude-opus-5-5',
    modelPlaceholder: 'claude-opus-5-5',
    modelHint: 'Identificador exacto del modelo de Claude, por ejemplo claude-opus-5-5 o claude-sonnet-5-5.',
    keyHint: 'Clave creada en console.anthropic.com → API Keys (empieza con sk-ant-). La suscripción de claude.ai no incluye clave de API.',
    testNotice: 'La prueba consulta el modelo en la API de Anthropic para validar la clave; no genera texto ni costo.',
    privacyNotice: 'La consulta y los datos operativos mínimos necesarios se envían a Anthropic para que Claude responda. Pueden aplicar las políticas de seguridad y retención de datos de Anthropic. Revisa sus condiciones y configura un límite de gasto en la Console de Anthropic antes de habilitar este uso.'
  }
];

export function providerOption(provider: string | null | undefined): AiProviderOption {
  return AI_PROVIDERS.find(x => x.id === provider) ?? AI_PROVIDERS[0];
}
