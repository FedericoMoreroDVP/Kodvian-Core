namespace Kodvian.Core.Application.Ai.Dtos;

public sealed class AiProviderConfigurationDto
{
    public bool IsConfigured { get; init; }
    public string Provider { get; init; } = "OpenAI";
    public string? Model { get; init; }
    public string? ApiKeyHint { get; init; }
    public bool IsConnectionVerified { get; init; }
    public DateTime? LastConnectionTestAtUtc { get; init; }
    public bool LiveDataApproved { get; init; }
    public Guid? LiveDataApprovedByUserId { get; init; }
    public DateTime? LiveDataApprovedAtUtc { get; init; }
    public string? LiveDataApprovalTermsVersion { get; init; }
    public string RequiredLiveDataApprovalTermsVersion { get; init; } = Kodvian.Core.Application.Ai.AiPrivacyPolicy.CurrentTermsVersion;
    public Guid Version { get; init; }
}

public sealed class AiProviderConfigurationUpdateRequest
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public Guid Version { get; set; }
}

public sealed class AiProviderConnectionTestDto
{
    public bool IsSuccessful { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? TestedAtUtc { get; init; }
}

public sealed class AiLiveDataApprovalRequest
{
    public bool AcknowledgeTerms { get; set; }
    public string TermsVersion { get; set; } = string.Empty;
}
