using Kodvian.Core.Application.Ai.Dtos;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiBriefingService
{
    /// <summary>Resumen y alertas según los permisos del usuario. No usa el proveedor de IA.</summary>
    Task<AiBriefingDto> GetAsync(AiExecutionContext actor, CancellationToken cancellationToken);
}
