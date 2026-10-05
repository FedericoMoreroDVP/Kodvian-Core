using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Meetings.Dtos;
using Kodvian.Core.Application.Meetings.Requests;

namespace Kodvian.Core.Application.Meetings.Abstractions;

public interface IMeetingService
{
    Task<PagedResultDto<MeetingDto>> GetPagedAsync(Guid userId, bool isAdministrator, MeetingListRequestDto request, CancellationToken cancellationToken = default);
    Task<MeetingDto?> GetByIdAsync(Guid userId, bool isAdministrator, Guid id, CancellationToken cancellationToken = default);
    Task<MeetingDto> CreateAsync(Guid userId, bool isAdministrator, MeetingUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<MeetingDto?> UpdateAsync(Guid userId, bool isAdministrator, Guid id, MeetingUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<MeetingDto?> CancelAsync(Guid userId, bool isAdministrator, Guid id, CancellationToken cancellationToken = default);
    Task<MeetingLookupsDto> GetLookupsAsync(Guid userId, bool isAdministrator, bool canWrite, CancellationToken cancellationToken = default);
}
