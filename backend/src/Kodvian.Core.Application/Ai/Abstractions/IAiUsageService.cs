using Kodvian.Core.Application.Ai.Dtos;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiUsageService
{
    Task<AiUsageReportDto> GetReportAsync(int days, CancellationToken cancellationToken);
    Task<AiUsageSettingsDto> UpdateSettingsAsync(Guid actorUserId, AiUsageSettingsDto request, CancellationToken cancellationToken);
    Task<AiBudgetStatusDto> GetBudgetStatusAsync(CancellationToken cancellationToken);
}
