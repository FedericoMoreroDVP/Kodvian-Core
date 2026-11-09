namespace Kodvian.Core.Domain.Entities;

public sealed class AiMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? ClientRequestId { get; set; }
    public string? TraceId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SourcesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public AiConversation? Conversation { get; set; }
}
