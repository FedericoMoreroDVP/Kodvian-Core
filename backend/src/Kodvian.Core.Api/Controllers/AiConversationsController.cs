using System.Security.Claims;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize(Policy = "AiOfficeUse")]
[Route("api/ai/conversations")]
public sealed class AiConversationsController(IAiConversationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<PagedResultDto<AiConversationDto>>>> GetPaged([FromQuery] PagedRequestDto request, CancellationToken cancellationToken)
    {
        var data = await service.GetPagedAsync(CreateActor(), request, cancellationToken);
        return Ok(ApiResponseDto<PagedResultDto<AiConversationDto>>.Ok(data, "Conversaciones obtenidas."));
    }

    [HttpPost]
    [RequestSizeLimit(4096)]
    [EnableRateLimiting("AiConversationMessages")]
    public async Task<ActionResult<ApiResponseDto<AiConversationDto>>> Create([FromBody] AiConversationCreateRequest request, CancellationToken cancellationToken)
    {
        var data = await service.CreateAsync(CreateActor(), request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, ApiResponseDto<AiConversationDto>.Ok(data, "Conversación creada."));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponseDto<AiConversationDetailDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var data = await service.GetByIdAsync(CreateActor(), id, cancellationToken);
        return data is null
            ? NotFound(ApiResponseDto<AiConversationDetailDto>.Fail("Conversación no encontrada."))
            : Ok(ApiResponseDto<AiConversationDetailDto>.Ok(data, "Conversación obtenida."));
    }

    [HttpPost("{id:guid}/messages")]
    [RequestSizeLimit(20000)]
    [EnableRateLimiting("AiConversationMessages")]
    public async Task<ActionResult<ApiResponseDto<AiSendMessageResultDto>>> SendMessage(Guid id, [FromBody] AiSendMessageRequest request, CancellationToken cancellationToken)
    {
        var data = await service.SendMessageAsync(CreateActor(), id, request, cancellationToken);
        return Ok(ApiResponseDto<AiSendMessageResultDto>.Ok(data, "Respuesta generada."));
    }

    /// <summary>
    /// Igual que <see cref="SendMessage"/>, pero responde con server-sent events: "status" (progreso por herramienta),
    /// "delta" (texto a medida que llega), "reset" (descartar texto intermedio), "done" (resultado final) o "error".
    /// </summary>
    [HttpPost("{id:guid}/messages/stream")]
    [RequestSizeLimit(20000)]
    [EnableRateLimiting("AiConversationMessages")]
    public async Task SendMessageStream(Guid id, [FromBody] AiSendMessageRequest request, CancellationToken cancellationToken)
    {
        var actor = CreateActor();
        var observer = new AiSseObserver(Response);
        try
        {
            var data = await service.SendMessageAsync(actor, id, request, observer, cancellationToken);
            await observer.WriteAsync("done", data, cancellationToken);
        }
        catch (Exception exception) when (observer.Started && exception is not OperationCanceledException)
        {
            // Con el stream abierto ya no se puede cambiar el código HTTP: el error viaja como evento.
            var (status, message) = exception switch
            {
                Kodvian.Core.Application.Ai.AiProviderUnavailableException => (503, exception.Message),
                Kodvian.Core.Application.Ai.AiLiveDataApprovalRequiredException => (409, exception.Message),
                Kodvian.Core.Application.Ai.AiConversationConflictException => (409, exception.Message),
                UnauthorizedAccessException => (403, exception.Message),
                KeyNotFoundException => (404, exception.Message),
                ArgumentException => (400, exception.Message),
                _ => (500, "No se pudo completar la consulta. La conversación quedó guardada para que puedas revisarla.")
            };
            await observer.WriteAsync("error", new { status, message }, CancellationToken.None);
        }
    }

    [HttpPost("{id:guid}/actions/{actionId:guid}/confirm")]
    [EnableRateLimiting("AiConversationMessages")]
    public async Task<ActionResult<ApiResponseDto<AiActionResolutionDto>>> ConfirmAction(Guid id, Guid actionId, CancellationToken cancellationToken)
    {
        var data = await service.ConfirmActionAsync(CreateActor(), id, actionId, cancellationToken);
        return Ok(ApiResponseDto<AiActionResolutionDto>.Ok(data, data.Action.Status == "Confirmed" ? "Acción confirmada." : "La acción no se pudo completar."));
    }

    [HttpPost("{id:guid}/messages/{messageId:guid}/actions/confirm-all")]
    [EnableRateLimiting("AiConversationMessages")]
    public async Task<ActionResult<ApiResponseDto<AiActionBatchResolutionDto>>> ConfirmAllActions(Guid id, Guid messageId, CancellationToken cancellationToken)
    {
        var data = await service.ConfirmAllActionsAsync(CreateActor(), id, messageId, cancellationToken);
        return Ok(ApiResponseDto<AiActionBatchResolutionDto>.Ok(data, "Acciones procesadas."));
    }

    [HttpPost("{id:guid}/actions/{actionId:guid}/undo")]
    [EnableRateLimiting("AiConversationMessages")]
    public async Task<ActionResult<ApiResponseDto<AiActionResolutionDto>>> UndoAction(Guid id, Guid actionId, CancellationToken cancellationToken)
    {
        var data = await service.UndoActionAsync(CreateActor(), id, actionId, cancellationToken);
        return Ok(ApiResponseDto<AiActionResolutionDto>.Ok(data, data.Action.Status == "Undone" ? "Acción deshecha." : "La acción no se pudo deshacer."));
    }

    [HttpPost("{id:guid}/actions/{actionId:guid}/cancel")]
    public async Task<ActionResult<ApiResponseDto<AiActionResolutionDto>>> CancelAction(Guid id, Guid actionId, CancellationToken cancellationToken)
    {
        var data = await service.CancelActionAsync(CreateActor(), id, actionId, cancellationToken);
        return Ok(ApiResponseDto<AiActionResolutionDto>.Ok(data, "Acción cancelada."));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponseDto<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(CreateActor(), id, cancellationToken);
        return deleted
            ? Ok(ApiResponseDto<object>.Ok(new { }, "Conversación eliminada."))
            : NotFound(ApiResponseDto<object>.Fail("Conversación no encontrada."));
    }

    private AiExecutionContext CreateActor()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(User.FindFirstValue(CustomClaimTypes.SessionVersion), out var sessionVersion))
            throw new UnauthorizedAccessException("La sesión no es válida.");
        return new AiExecutionContext
        {
            UserId = userId,
            SessionVersion = sessionVersion,
            Roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.Ordinal),
            Permissions = User.FindAll(CustomClaimTypes.Permission).Select(x => x.Value).ToHashSet(StringComparer.Ordinal)
        };
    }
}
