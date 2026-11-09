namespace Kodvian.Core.Domain.Entities;

public sealed class AiRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string Status { get; set; } = "Running";
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = string.Empty;
    public Guid ProviderConfigurationVersion { get; set; }
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
    public int ToolCallCount { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public AiConversation? Conversation { get; set; }
}
