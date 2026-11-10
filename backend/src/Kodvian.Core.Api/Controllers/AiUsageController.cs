using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

/// <summary>Tablero de uso del asistente, precios por millón de tokens y tope de gasto mensual (solo Administradores).</summary>
[ApiController]
[Authorize(Policy = "AdministratorOnly")]
[Route("api/ai/usage")]
public sealed class AiUsageController(IAiUsageService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<AiUsageReportDto>>> Get([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        Ok(ApiResponseDto<AiUsageReportDto>.Ok(await service.GetReportAsync(days, cancellationToken), "Uso obtenido."));

    [HttpPut("settings")]
    [RequestSizeLimit(2048)]
    public async Task<ActionResult<ApiResponseDto<AiUsageSettingsDto>>> UpdateSettings([FromBody] AiUsageSettingsDto request, CancellationToken cancellationToken) =>
        Ok(ApiResponseDto<AiUsageSettingsDto>.Ok(await service.UpdateSettingsAsync(AiActorFactory.FromUser(User).UserId, request, cancellationToken), "Configuración de uso guardada."));
}
