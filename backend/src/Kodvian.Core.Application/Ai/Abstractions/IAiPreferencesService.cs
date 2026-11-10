using Kodvian.Core.Application.Ai.Dtos;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiPreferencesService
{
    Task<AiUserPreferencesDto> GetAsync(AiExecutionContext actor, CancellationToken cancellationToken);
    Task<AiUserPreferencesDto> UpdateAsync(AiExecutionContext actor, AiUserPreferencesDto request, CancellationToken cancellationToken);
}
