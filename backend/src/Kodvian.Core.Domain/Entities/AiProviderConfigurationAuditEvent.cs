namespace Kodvian.Core.Domain.Entities;

public sealed class AiProviderConfigurationAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? TermsVersion { get; set; }
    public bool CredentialReplaced { get; set; }
    public bool? ConnectionSucceeded { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
