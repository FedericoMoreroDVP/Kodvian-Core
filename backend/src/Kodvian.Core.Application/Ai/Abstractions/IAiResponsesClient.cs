using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai.Dtos;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiResponsesClient
{
    Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken);
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
}
