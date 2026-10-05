using System.Security.Claims;
using Kodvian.Core.Api.Validation;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Meetings.Abstractions;
using Kodvian.Core.Application.Meetings.Dtos;
using Kodvian.Core.Application.Meetings.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize(Policy = "MeetingsRead")]
[Route("api/meetings")]
public class MeetingsController : ControllerBase
{
    private readonly IMeetingService _meetingService;
    public MeetingsController(IMeetingService meetingService) => _meetingService = meetingService;

    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<PagedResultDto<MeetingDto>>>> GetPaged([FromQuery] MeetingListRequestDto request, CancellationToken cancellationToken)
    {
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
            return BadRequest(ApiResponseDto<PagedResultDto<MeetingDto>>.Fail("El rango de fechas es inválido"));
        if (!string.IsNullOrWhiteSpace(request.Priority) && !Enum.TryParse<Kodvian.Core.Domain.Enums.MeetingPriority>(request.Priority, true, out _))
            return BadRequest(ApiResponseDto<PagedResultDto<MeetingDto>>.Fail("La prioridad de la reunión no es válida"));
        if (!string.IsNullOrWhiteSpace(request.Status) && !Enum.TryParse<Kodvian.Core.Domain.Enums.MeetingStatus>(request.Status, true, out _))
            return BadRequest(ApiResponseDto<PagedResultDto<MeetingDto>>.Fail("El estado de la reunión no es válido"));
        var data = await _meetingService.GetPagedAsync(GetUserId(), IsAdministrator(), request, cancellationToken);
        return Ok(ApiResponseDto<PagedResultDto<MeetingDto>>.Ok(data, "Reuniones obtenidas correctamente"));
    }

    [HttpGet("lookups")]
    public async Task<ActionResult<ApiResponseDto<MeetingLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var canWrite = User.HasClaim(CustomClaimTypes.Permission, PermissionCodes.MeetingsWrite);
        var data = await _meetingService.GetLookupsAsync(GetUserId(), IsAdministrator(), canWrite, cancellationToken);
        return Ok(ApiResponseDto<MeetingLookupsDto>.Ok(data, "Datos de referencia obtenidos correctamente"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponseDto<MeetingDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var data = await _meetingService.GetByIdAsync(GetUserId(), IsAdministrator(), id, cancellationToken);
        return data is null ? NotFound(ApiResponseDto<MeetingDto>.Fail("Reunión no encontrada")) : Ok(ApiResponseDto<MeetingDto>.Ok(data, "Reunión obtenida correctamente"));
    }

    [HttpPost]
    [Authorize(Policy = "MeetingsWrite")]
    public async Task<ActionResult<ApiResponseDto<MeetingDto>>> Create([FromBody] MeetingUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var error = RequestValidation.Validate(request);
        if (error is not null) return BadRequest(ApiResponseDto<MeetingDto>.Fail(error));
        var data = await _meetingService.CreateAsync(GetUserId(), IsAdministrator(), request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, ApiResponseDto<MeetingDto>.Ok(data, "La reunión se creó correctamente"));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "MeetingsWrite")]
    public async Task<ActionResult<ApiResponseDto<MeetingDto>>> Update(Guid id, [FromBody] MeetingUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var error = RequestValidation.Validate(request);
        if (error is not null) return BadRequest(ApiResponseDto<MeetingDto>.Fail(error));
        var data = await _meetingService.UpdateAsync(GetUserId(), IsAdministrator(), id, request, cancellationToken);
        return data is null ? NotFound(ApiResponseDto<MeetingDto>.Fail("Reunión no encontrada")) : Ok(ApiResponseDto<MeetingDto>.Ok(data, "La reunión se actualizó correctamente"));
    }

    [HttpPatch("{id:guid}/cancel")]
    [Authorize(Policy = "MeetingsCancel")]
    public async Task<ActionResult<ApiResponseDto<MeetingDto>>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var data = await _meetingService.CancelAsync(GetUserId(), IsAdministrator(), id, cancellationToken);
        return data is null ? NotFound(ApiResponseDto<MeetingDto>.Fail("Reunión no encontrada")) : Ok(ApiResponseDto<MeetingDto>.Ok(data, "La reunión se canceló correctamente"));
    }

    private Guid GetUserId()
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return id;
        throw new UnauthorizedAccessException("Token inválido");
    }
    private bool IsAdministrator() => User.IsInRole(RoleNames.Administrator);
}
