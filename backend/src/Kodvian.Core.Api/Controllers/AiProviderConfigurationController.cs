using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdministratorOnly")]
[Route("api/ai/provider-configuration")]
public sealed class AiProviderConfigurationController(IAiProviderConfigurationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<AiProviderConfigurationDto>>> Get(CancellationToken cancellationToken)
    {
        var configuration = await service.GetAsync(cancellationToken);
        return Ok(ApiResponseDto<AiProviderConfigurationDto>.Ok(configuration, "Configuración de IA obtenida."));
    }

    [HttpPut]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<ApiResponseDto<AiProviderConfigurationDto>>> Update(
        [FromBody] AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken)
    {
        var configuration = await service.UpdateAsync(request, cancellationToken);
        return Ok(ApiResponseDto<AiProviderConfigurationDto>.Ok(configuration, "Configuración guardada. Verifica la conexión antes de usarla."));
    }

    [HttpPost("test")]
    [EnableRateLimiting("AiProviderConnectionTest")]
    [RequestSizeLimit(1024)]
    public async Task<ActionResult<ApiResponseDto<AiProviderConnectionTestDto>>> Test(CancellationToken cancellationToken)
    {
        var result = await service.TestConnectionAsync(cancellationToken);
        return Ok(ApiResponseDto<AiProviderConnectionTestDto>.Ok(result, result.Message));
    }

    [HttpPost("approve-live-data")]
    [RequestSizeLimit(1024)]
    public async Task<ActionResult<ApiResponseDto<AiProviderConfigurationDto>>> ApproveLiveData([FromBody] AiLiveDataApprovalRequest request, CancellationToken cancellationToken)
    {
        var configuration = await service.ApproveLiveDataAsync(request, cancellationToken);
        return Ok(ApiResponseDto<AiProviderConfigurationDto>.Ok(configuration, "Se registró la aprobación para enviar datos autorizados al proveedor."));
    }

    [HttpDelete("approve-live-data")]
    public async Task<ActionResult<ApiResponseDto<AiProviderConfigurationDto>>> RevokeLiveDataApproval(CancellationToken cancellationToken)
    {
        var configuration = await service.RevokeLiveDataApprovalAsync(cancellationToken);
        return Ok(ApiResponseDto<AiProviderConfigurationDto>.Ok(configuration, "Se revocó la aprobación para enviar datos reales."));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponseDto<object>>> Delete([FromQuery] Guid expectedVersion, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(expectedVersion, cancellationToken);
        return Ok(ApiResponseDto<object>.Ok(new { }, "Se eliminó la configuración del proveedor de IA."));
    }
}
