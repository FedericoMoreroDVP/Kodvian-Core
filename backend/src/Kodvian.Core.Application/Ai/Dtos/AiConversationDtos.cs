namespace Kodvian.Core.Application.Ai.Dtos;

public sealed class AiConversationDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public int MessageCount { get; init; }
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
}

public sealed class AiPendingActionDto
{
    public Guid Id { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyCollection<AiActionDetailDto> Details { get; init; } = [];
    /// <summary>Pending, Executing, Confirmed, Cancelled, Expired o Failed.</summary>
    public string Status { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? ResolvedAtUtc { get; init; }
    public string? ResultMessage { get; init; }
    public string? ResultLink { get; init; }
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
