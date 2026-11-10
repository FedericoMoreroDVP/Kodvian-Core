namespace Kodvian.Core.Application.Ai.Dtos;

public sealed class AiConversationDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public int MessageCount { get; init; }
    public bool IsPinned { get; init; }
}

public sealed class AiConversationListRequest : Kodvian.Core.Application.Common.Models.PagedRequestDto
{
    /// <summary>Busca en el título y en el texto de los mensajes.</summary>
    public string? Search { get; set; }
}

public sealed class AiConversationUpdateRequest
{
    public string? Title { get; set; }
    public bool? IsPinned { get; set; }
}

public sealed class AiMessageFeedbackRequest
{
    /// <summary>1 (útil), -1 (no útil) o 0 para quitar la valoración.</summary>
    public int Value { get; set; }
}

/// <summary>Entidad que consultó el asistente (tipo, nombre e id), para mostrarla como tarjeta clickeable.</summary>
public sealed class AiEntityReferenceDto
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid Id { get; init; }
}

public sealed class AiSourceReferenceDto
{
    public string ToolName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public DateTime AsOfUtc { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Currency { get; init; }
    public string? Link { get; init; }
}

public sealed class AiMessageDto
{
    public Guid Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlyCollection<AiSourceReferenceDto> Sources { get; init; } = [];
    /// <summary>False cuando la respuesta no se apoyó en ninguna consulta a datos de Kodvian.</summary>
    public bool Grounded { get; init; } = true;
    public IReadOnlyCollection<AiPendingActionDto> PendingActions { get; init; } = [];
    /// <summary>Sugerencias de seguimiento (2 o 3) que el usuario puede tocar para continuar.</summary>
    public IReadOnlyCollection<string> Suggestions { get; init; } = [];
    /// <summary>Tareas, reuniones, proyectos y clientes consultados en la respuesta.</summary>
    public IReadOnlyCollection<AiEntityReferenceDto> References { get; init; } = [];
    /// <summary>Gráfico opcional (ingresos vs gastos por moneda).</summary>
    public System.Text.Json.Nodes.JsonObject? Visual { get; init; }
    public int? Feedback { get; init; }
}

public sealed class AiPendingActionDto
{
    public Guid Id { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyCollection<AiActionDetailDto> Details { get; init; } = [];
    /// <summary>Pending, Executing, Confirmed, Cancelled, Expired, Failed o Undone.</summary>
    public string Status { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? ResolvedAtUtc { get; init; }
    public string? ResultMessage { get; init; }
    public string? ResultLink { get; init; }
    /// <summary>True durante los minutos en que la acción confirmada todavía se puede deshacer.</summary>
    public bool CanUndo { get; init; }
    public DateTime? UndoAvailableUntilUtc { get; init; }
}

public sealed class AiActionBatchResolutionDto
{
    public IReadOnlyCollection<AiPendingActionDto> Actions { get; init; } = [];
    public AiMessageDto? Message { get; init; }
}

public sealed class AiActionResolutionDto
{
    public AiPendingActionDto Action { get; init; } = new();
    /// <summary>Mensaje que se agregó a la conversación con el resultado (solo al confirmar).</summary>
    public AiMessageDto? Message { get; init; }
}

public sealed class AiConversationDetailDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public int MessageCount { get; init; }
    public IReadOnlyCollection<AiMessageDto> Messages { get; init; } = [];
}

public sealed class AiConversationCreateRequest
{
    public string? Title { get; set; }
}

public sealed class AiSendMessageRequest
{
    public Guid ClientRequestId { get; set; }
    public string Content { get; set; } = string.Empty;
    /// <summary>Pantalla desde la que se abrió el asistente (por ejemplo "/proyectos"), para dar contexto.</summary>
    public string? PageContext { get; set; }
}

public sealed class AiSendMessageResultDto
{
    public AiMessageDto UserMessage { get; init; } = new();
    public AiMessageDto AssistantMessage { get; init; } = new();
    public Guid RunId { get; init; }
    public string TraceId { get; init; } = string.Empty;
}

public sealed class AiExecutionContext
{
    public Guid UserId { get; init; }
    public Guid SessionVersion { get; init; }
    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
