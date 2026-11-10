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
}

public sealed class AiToolExecutionResultDto
{
    public JsonObject Data { get; init; } = new();
    public AiSourceReferenceDto Source { get; init; } = new();
}
