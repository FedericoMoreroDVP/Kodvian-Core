namespace Kodvian.Core.Domain.Entities;

public sealed class AiAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? RunId { get; set; }
    public string? ToolName { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
