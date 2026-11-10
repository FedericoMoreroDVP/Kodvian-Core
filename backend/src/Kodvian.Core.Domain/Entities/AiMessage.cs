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
    /// <summary>False cuando la respuesta del asistente no se apoyó en ninguna herramienta.</summary>
    public bool Grounded { get; set; } = true;
    public string SuggestionsJson { get; set; } = "[]";
    /// <summary>Referencias (id y nombre) de lo que consultaron las herramientas en este turno, para retomarlas en los siguientes.</summary>
    public string? ContextJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public AiConversation? Conversation { get; set; }
}
