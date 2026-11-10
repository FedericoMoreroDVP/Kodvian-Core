using System.Security.Claims;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

/// <summary>Resumen y alertas que se muestran al abrir Oficina Inteligente (sin IA).</summary>
[ApiController]
[Authorize(Policy = "AiOfficeUse")]
[Route("api/ai/briefing")]
public sealed class AiBriefingController(IAiBriefingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<AiBriefingDto>>> Get(CancellationToken cancellationToken)
    {
        var data = await service.GetAsync(AiActorFactory.FromUser(User), cancellationToken);
        return Ok(ApiResponseDto<AiBriefingDto>.Ok(data, "Resumen obtenido."));
    }
}

internal static class AiActorFactory
{
    public static AiExecutionContext FromUser(ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(user.FindFirstValue(CustomClaimTypes.SessionVersion), out var sessionVersion))
            throw new UnauthorizedAccessException("La sesión no es válida.");
        return new AiExecutionContext
        {
            UserId = userId,
            SessionVersion = sessionVersion,
            Roles = user.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.Ordinal),
            Permissions = user.FindAll(CustomClaimTypes.Permission).Select(x => x.Value).ToHashSet(StringComparer.Ordinal)
        };
    }
}
