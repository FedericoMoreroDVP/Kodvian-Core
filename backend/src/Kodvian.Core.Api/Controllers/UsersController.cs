using Kodvian.Core.Application.Administration;
using Kodvian.Core.Application.Administration.Dtos;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = "AdministratorOnly")]
public class UsersController(IUserAdministrationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<PagedResultDto<UserListItemDto>>>> Get([FromQuery] UserListRequestDto request, CancellationToken ct) =>
        Ok(ApiResponseDto<PagedResultDto<UserListItemDto>>.Ok(await service.GetAsync(request, ct), "Usuarios obtenidos correctamente"));

    [HttpGet("roles")]
    public ActionResult<ApiResponseDto<string[]>> GetRoles() =>
        Ok(ApiResponseDto<string[]>.Ok(RolePermissionMap.AvailableRoles, "Roles disponibles"));

    [HttpPut("{id:guid}/roles")]
    public async Task<ActionResult<ApiResponseDto<UserListItemDto>>> UpdateRoles(Guid id, UserRolesUpdateRequestDto request, CancellationToken ct)
    {
        var result = await service.UpdateRolesAsync(id, request, ct);
        return result is null ? NotFound(ApiResponseDto<UserListItemDto>.Fail("Usuario no encontrado"))
            : Ok(ApiResponseDto<UserListItemDto>.Ok(result, "Roles actualizados. El usuario deberá iniciar sesión nuevamente."));
    }
}
