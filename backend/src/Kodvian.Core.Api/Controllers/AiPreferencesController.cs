using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

/// <summary>Preferencias personales del asistente (moneda, duración de reuniones, estilo, indicaciones).</summary>
[ApiController]
[Authorize(Policy = "AiOfficeUse")]
[Route("api/ai/preferences")]
public sealed class AiPreferencesController(IAiPreferencesService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<AiUserPreferencesDto>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponseDto<AiUserPreferencesDto>.Ok(await service.GetAsync(AiActorFactory.FromUser(User), cancellationToken), "Preferencias obtenidas."));

    [HttpPut]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<ApiResponseDto<AiUserPreferencesDto>>> Update([FromBody] AiUserPreferencesDto request, CancellationToken cancellationToken) =>
        Ok(ApiResponseDto<AiUserPreferencesDto>.Ok(await service.UpdateAsync(AiActorFactory.FromUser(User), request, cancellationToken), "Preferencias guardadas."));
}
