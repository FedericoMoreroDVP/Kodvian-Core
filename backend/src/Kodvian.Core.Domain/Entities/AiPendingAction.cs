namespace Kodvian.Core.Domain.Entities;

/// <summary>
/// Acción de escritura propuesta por el asistente que espera la confirmación del usuario.
/// El asistente nunca escribe: solo deja la propuesta y el usuario la confirma o la cancela.
/// </summary>
public sealed class AiPendingAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid RunId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid ActorUserId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "[]";
    /// <summary>Pending, Executing, Confirmed, Cancelled, Expired o Failed.</summary>
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResultMessage { get; set; }
    public string? ResultLink { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public AiConversation? Conversation { get; set; }
}
