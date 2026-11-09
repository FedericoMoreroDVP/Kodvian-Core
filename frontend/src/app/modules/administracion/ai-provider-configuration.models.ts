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
