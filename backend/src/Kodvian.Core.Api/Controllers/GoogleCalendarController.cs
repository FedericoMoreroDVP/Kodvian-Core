using System.Security.Claims;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Integrations.GoogleCalendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

/// <summary>Conexión de la cuenta de Google de cada usuario para agendar sus reuniones en Google Calendar.</summary>
[ApiController]
[Authorize]
[Route("api/integrations/google-calendar")]
public sealed class GoogleCalendarController(IGoogleCalendarService service) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponseDto<GoogleCalendarStatusDto>>> Status(CancellationToken cancellationToken) =>
        Ok(ApiResponseDto<GoogleCalendarStatusDto>.Ok(await service.GetStatusAsync(UserId(), cancellationToken), "Estado obtenido."));

    /// <summary>Devuelve la URL de Google a la que el navegador tiene que ir para autorizar el acceso.</summary>
    [HttpPost("connect")]
    public async Task<ActionResult<ApiResponseDto<object>>> Connect(CancellationToken cancellationToken)
    {
        var status = await service.GetStatusAsync(UserId(), cancellationToken);
        if (!status.Enabled)
            return Conflict(ApiResponseDto<object>.Fail("La integración con Google Calendar todavía no está configurada en el servidor."));
        return Ok(ApiResponseDto<object>.Ok(new { authorizationUrl = service.BuildAuthorizationUrl(UserId()) }, "Redirigiendo a Google."));
    }

    /// <summary>
    /// Google vuelve acá después de autorizar. No exige la sesión de Kodvian porque el usuario queda identificado
    /// por el state firmado, cifrado y con vencimiento de 10 minutos que se generó al iniciar la conexión.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        var result = await service.HandleCallbackAsync(code, state, error, cancellationToken);
        return Redirect(result.Success ? "/agenda?googleCalendar=connected" : $"/agenda?googleCalendar=error&reason={Uri.EscapeDataString(result.Reason ?? "unknown")}");
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponseDto<object>>> Disconnect(CancellationToken cancellationToken)
    {
        await service.DisconnectAsync(UserId(), cancellationToken);
        return Ok(ApiResponseDto<object>.Ok(new { }, "Google Calendar desconectado."));
    }

    /// <summary>Reintenta sincronizar una reunión que quedó sin sincronizar.</summary>
    [HttpPost("meetings/{meetingId:guid}/sync")]
    public async Task<ActionResult<ApiResponseDto<GoogleCalendarSyncResultDto>>> Sync(Guid meetingId, CancellationToken cancellationToken)
    {
        var result = await service.RetryMeetingAsync(UserId(), User.IsInRole(RoleNames.Administrator), meetingId, cancellationToken);
        return result is null
            ? NotFound(ApiResponseDto<GoogleCalendarSyncResultDto>.Fail("Reunión no encontrada."))
            : Ok(ApiResponseDto<GoogleCalendarSyncResultDto>.Ok(result, result.Status == "Synced" ? "Reunión sincronizada." : "No se pudo sincronizar la reunión."));
    }

    private Guid UserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : throw new UnauthorizedAccessException("La sesión no es válida.");
}
