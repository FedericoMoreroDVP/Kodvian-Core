using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;

namespace Kodvian.Core.Application.Developers.Abstractions;

public interface IProjectDeveloperContractService
{
    Task<IReadOnlyCollection<ProjectDeveloperContractDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DeveloperContractSummaryDto>> GetByDeveloperSummaryAsync(Guid developerId, int year, CancellationToken cancellationToken = default);
    Task<ProjectDeveloperContractDto> CreateAsync(Guid projectId, ProjectDeveloperContractUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<ProjectDeveloperContractDto?> UpdateAsync(Guid id, ProjectDeveloperContractUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ContractLedgerDto?> GetLedgerAsync(Guid contractId, int year, CancellationToken cancellationToken = default);
}
