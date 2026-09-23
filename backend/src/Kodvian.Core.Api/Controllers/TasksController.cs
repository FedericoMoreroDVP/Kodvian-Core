using System.Security.Claims;
using Kodvian.Core.Api.Validation;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Tasks.Abstractions;
using Kodvian.Core.Application.Tasks.Dtos;
using Kodvian.Core.Application.Tasks.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize(Policy = "TasksRead")]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly ITaskService _taskService;

    public TasksController(ITaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<PagedResultDto<TaskListItemDto>>>> GetPaged([FromQuery] TaskListRequestDto request, CancellationToken cancellationToken)
    {
        if (request.DueDateFrom.HasValue && request.DueDateTo.HasValue && request.DueDateFrom > request.DueDateTo)
        {
            return BadRequest(ApiResponseDto<PagedResultDto<TaskListItemDto>>.Fail("El rango de vencimiento es inválido"));
        }

        var data = await _taskService.GetPagedAsync(request, cancellationToken);
        return Ok(ApiResponseDto<PagedResultDto<TaskListItemDto>>.Ok(data, "Tareas obtenidas correctamente"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponseDto<TaskDetailDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var data = await _taskService.GetByIdAsync(id, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<TaskDetailDto>.Fail("Tarea no encontrada"));
        }

        return Ok(ApiResponseDto<TaskDetailDto>.Ok(data, "Detalle de tarea obtenido correctamente"));
    }

    [HttpGet("kanban")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<TaskKanbanColumnDto>>>> GetKanban([FromQuery] TaskListRequestDto request, CancellationToken cancellationToken)
    {
        var data = await _taskService.GetKanbanAsync(request, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<TaskKanbanColumnDto>>.Ok(data, "Tablero de tareas obtenido correctamente"));
    }

    [HttpGet("lookups")]
    public async Task<ActionResult<ApiResponseDto<TaskLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var data = await _taskService.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponseDto<TaskLookupsDto>.Ok(data, "Datos de referencia obtenidos correctamente"));
    }

    [HttpPost]
    [Authorize(Policy = "TasksWrite")]
    public async Task<ActionResult<ApiResponseDto<TaskDetailDto>>> Create([FromBody] TaskUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<TaskDetailDto>.Fail(validationError));
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var createdById))
        {
            return Unauthorized(ApiResponseDto<TaskDetailDto>.Fail("Token inválido"));
        }

        var data = await _taskService.CreateAsync(createdById, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, ApiResponseDto<TaskDetailDto>.Ok(data, "La tarea se creó correctamente"));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "TasksWrite")]
    public async Task<ActionResult<ApiResponseDto<TaskDetailDto>>> Update(Guid id, [FromBody] TaskUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<TaskDetailDto>.Fail(validationError));
        }

        var data = await _taskService.UpdateAsync(id, request, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<TaskDetailDto>.Fail("Tarea no encontrada"));
        }

        return Ok(ApiResponseDto<TaskDetailDto>.Ok(data, "La tarea se actualizó correctamente"));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "TasksWrite")]
    public async Task<ActionResult<ApiResponseDto<TaskDetailDto>>> UpdateStatus(Guid id, [FromBody] TaskStatusUpdateRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<TaskDetailDto>.Fail(validationError));
        }

        var data = await _taskService.UpdateStatusAsync(id, request, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<TaskDetailDto>.Fail("Tarea no encontrada"));
        }

        return Ok(ApiResponseDto<TaskDetailDto>.Ok(data, "El estado de la tarea se actualizó correctamente"));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "TasksWrite")]
    public async Task<ActionResult<ApiResponseDto<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await _taskService.DeleteAsync(id, cancellationToken))
            return NotFound(ApiResponseDto<object>.Fail("Tarea no encontrada"));

        return Ok(ApiResponseDto<object>.Ok(new { }, "La tarea cancelada y sus adjuntos se eliminaron permanentemente"));
    }

}
