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
