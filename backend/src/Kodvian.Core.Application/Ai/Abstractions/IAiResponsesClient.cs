using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai.Dtos;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiResponsesClient
{
    Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken);

    /// <summary>
    /// Igual que <see cref="CreateAsync"/>, pero avisa cada fragmento de texto a medida que llega.
    /// La implementación por defecto no transmite: espera la respuesta completa y la avisa de una vez.
    /// </summary>
    async Task<AiModelResponseDto> CreateStreamingAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, Func<string, CancellationToken, Task> onTextDelta, CancellationToken cancellationToken)
    {
        var response = await CreateAsync(expectedConfigurationVersion, input, tools, cancellationToken);
        if (!string.IsNullOrEmpty(response.OutputText)) await onTextDelta(response.OutputText, cancellationToken);
        return response;
    }
}

/// <summary>Recibe el progreso de una ejecución del asistente para mostrarlo en vivo.</summary>
public interface IAiRunObserver
{
    /// <summary>Estado general ("thinking") o herramienta que se está ejecutando, con una etiqueta para el usuario.</summary>
    Task OnStatusAsync(string status, string label, string? toolName, CancellationToken cancellationToken);
    Task OnTextDeltaAsync(string text, CancellationToken cancellationToken);
    /// <summary>El texto transmitido era intermedio (el modelo siguió con herramientas): hay que descartarlo.</summary>
    Task OnTextResetAsync(CancellationToken cancellationToken);
}

public interface IAiToolExecutor
{
    IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor);
    Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid expectedConfigurationVersion, string traceId, string toolName, JsonObject arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Ejecuta una acción que el usuario confirmó. Revalida permisos, sesión y argumentos antes de escribir.
    /// Las validaciones de dominio se informan con <see cref="Kodvian.Core.Application.Ai.AiToolRejectedException"/>.
    /// </summary>
    Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Revierte una acción confirmada si todavía es seguro (nadie la modificó después).
    /// Si no se puede, informa el motivo con <see cref="Kodvian.Core.Application.Ai.AiToolRejectedException"/>.
    /// </summary>
    Task<AiActionOutcomeDto> UndoActionAsync(AiExecutionContext actor, Guid actionId, string toolName, Guid entityId, JsonObject undoData, CancellationToken cancellationToken) =>
        throw new Kodvian.Core.Application.Ai.AiToolRejectedException("Esta acción no se puede deshacer.");
}
