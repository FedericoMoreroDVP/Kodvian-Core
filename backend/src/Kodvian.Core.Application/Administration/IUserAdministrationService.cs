using Kodvian.Core.Application.Administration.Dtos;
using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Administration;

public class UserListRequestDto : PagedRequestDto
{
    public string? Search { get; set; }
}

public class UserRolesUpdateRequestDto
{
    public string[] Roles { get; set; } = [];
    public Guid ExpectedVersion { get; set; }
}

public interface IUserAdministrationService
{
    Task<PagedResultDto<UserListItemDto>> GetAsync(UserListRequestDto request, CancellationToken ct);
    Task<UserListItemDto?> UpdateRolesAsync(Guid id, UserRolesUpdateRequestDto request, CancellationToken ct);
}
