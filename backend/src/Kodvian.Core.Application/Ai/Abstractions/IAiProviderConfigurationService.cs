using Kodvian.Core.Application.Ai.Dtos;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiProviderConfigurationService
{
    Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken);
    Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken);
    Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken);
    Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken);
    Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken);
}
