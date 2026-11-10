using System.Text.Json.Nodes;

namespace Kodvian.Core.Application.Ai.Dtos;

public sealed class AiModelResponseDto
{
    public string Status { get; init; } = string.Empty;
    public string OutputText { get; init; } = string.Empty;
    public JsonArray OutputItems { get; init; } = [];
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

public sealed class AiToolDefinitionDto
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public JsonObject Parameters { get; init; } = new();
    public IReadOnlyCollection<string> RequiredPermissions { get; init; } = [];
    /// <summary>Las herramientas de acción no escriben: preparan una propuesta que el usuario confirma.</summary>
    public bool RequiresConfirmation { get; init; }
    /// <summary>Además de RequiredPermissions, el usuario necesita al menos uno de estos (vacío = no aplica).</summary>
    public IReadOnlyCollection<string> AnyOfPermissions { get; init; } = [];
}

public sealed class AiToolExecutionResultDto
{
    public JsonObject Data { get; init; } = new();
    /// <summary>Fuente consultada; null para herramientas de acción, que no leen datos para responder.</summary>
    public AiSourceReferenceDto? Source { get; init; }
    /// <summary>Propuesta de escritura que el usuario tiene que confirmar.</summary>
    public AiActionProposalDto? Proposal { get; init; }
}

public sealed class AiActionDetailDto
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>Acción validada y con referencias resueltas, lista para que el usuario la confirme.</summary>
public sealed class AiActionProposalDto
{
    public string ToolName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<AiActionDetailDto> Details { get; init; } = [];
    public JsonObject Arguments { get; init; } = new();
}

/// <summary>Resultado de ejecutar una acción confirmada.</summary>
public sealed class AiActionOutcomeDto
{
    public string Message { get; init; } = string.Empty;
    public string? Link { get; init; }
    public AiSourceReferenceDto? Source { get; init; }
    /// <summary>Entidad creada o modificada.</summary>
    public Guid? EntityId { get; init; }
    /// <summary>Estado necesario para revertir la acción; null si no se puede deshacer de forma segura.</summary>
    public JsonObject? UndoData { get; init; }
}
