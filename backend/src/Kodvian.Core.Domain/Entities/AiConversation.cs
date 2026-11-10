namespace Kodvian.Core.Domain.Entities;

public sealed class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
    public ICollection<AiRun> Runs { get; set; } = new List<AiRun>();
    public ICollection<AiPendingAction> PendingActions { get; set; } = new List<AiPendingAction>();
}
