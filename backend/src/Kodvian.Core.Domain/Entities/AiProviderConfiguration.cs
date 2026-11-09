namespace Kodvian.Core.Domain.Entities;

public sealed class AiProviderConfiguration
{
    public int Id { get; set; } = 1;
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = string.Empty;
    public string ProtectedApiKey { get; set; } = string.Empty;
    public string ApiKeyLastFour { get; set; } = string.Empty;
    public bool IsConnectionVerified { get; set; }
    public DateTime? LastConnectionTestAtUtc { get; set; }
    public string? LiveDataApprovalTermsVersion { get; set; }
    public Guid? LiveDataApprovedByUserId { get; set; }
    public DateTime? LiveDataApprovedAtUtc { get; set; }
    public string? LiveDataApprovedProvider { get; set; }
    public string? LiveDataApprovedModel { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
