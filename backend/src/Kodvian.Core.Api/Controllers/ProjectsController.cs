using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Projects.Abstractions;
using Kodvian.Core.Application.Projects.Dtos;
using Kodvian.Core.Application.Projects.Requests;
using Kodvian.Core.Api.Validation;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize(Policy = "ProjectsRead")]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponseDto<PagedResultDto<ProjectListItemDto>>>> GetPaged(
        [FromQuery] ProjectListRequestDto request,
        CancellationToken cancellationToken)
    {
        var data = await _projectService.GetPagedAsync(request, cancellationToken);
        return Ok(ApiResponseDto<PagedResultDto<ProjectListItemDto>>.Ok(data, "Proyectos obtenidos correctamente"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponseDto<ProjectDetailDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var data = await _projectService.GetByIdAsync(id, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<ProjectDetailDto>.Fail("Proyecto no encontrado"));
        }

        return Ok(ApiResponseDto<ProjectDetailDto>.Ok(data, "Detalle del proyecto obtenido correctamente"));
    }

    [HttpGet("lookups")]
    public async Task<ActionResult<ApiResponseDto<ProjectLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var data = await _projectService.GetLookupsAsync(cancellationToken);
        return Ok(ApiResponseDto<ProjectLookupsDto>.Ok(data, "Datos de referencia obtenidos correctamente"));
    }

    [HttpPost]
    [Authorize(Policy = "ProjectsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDetailDto>>> Create([FromBody] ProjectUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<ProjectDetailDto>.Fail(validationError));
        }

        var data = await _projectService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, ApiResponseDto<ProjectDetailDto>.Ok(data, "El proyecto se creó correctamente"));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ProjectsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDetailDto>>> Update(Guid id, [FromBody] ProjectUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<ProjectDetailDto>.Fail(validationError));
        }

        var data = await _projectService.UpdateAsync(id, request, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<ProjectDetailDto>.Fail("Proyecto no encontrado"));
        }

        return Ok(ApiResponseDto<ProjectDetailDto>.Ok(data, "El proyecto se actualizó correctamente"));
    }

    [HttpGet("{id:guid}/drive-link")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    public async Task<ActionResult<ApiResponseDto<ProjectDriveLinkDto>>> GetDriveLink(Guid id, CancellationToken cancellationToken)
    {
        var data = await _projectService.GetDriveLinkAsync(id, cancellationToken);
        if (data is null) return NotFound(ApiResponseDto<ProjectDriveLinkDto>.Fail("Proyecto no encontrado"));
        return Ok(ApiResponseDto<ProjectDriveLinkDto>.Ok(data, "Enlace obtenido correctamente"));
    }

    [HttpPut("{id:guid}/drive-link")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    [Authorize(Policy = "ProjectsDocumentsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDriveLinkDto>>> UpdateDriveLink(Guid id, [FromBody] ProjectDriveLinkRequestDto request, CancellationToken cancellationToken)
    {
        var data = await _projectService.UpdateDriveLinkAsync(id, request, cancellationToken);
        if (data is null) return NotFound(ApiResponseDto<ProjectDriveLinkDto>.Fail("Proyecto no encontrado"));
        return Ok(ApiResponseDto<ProjectDriveLinkDto>.Ok(data, data.GoogleDriveFolderUrl is null ? "Enlace quitado correctamente" : "Enlace guardado correctamente"));
    }

    [HttpGet("document-types")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<ProjectDocumentTypeDto>>>> GetDocumentTypes(CancellationToken cancellationToken)
    {
        var data = await _projectService.GetDocumentTypesAsync(cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<ProjectDocumentTypeDto>>.Ok(data, "Tipos de documento obtenidos correctamente"));
    }

    [HttpGet("{id:guid}/documents")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<ProjectDocumentListItemDto>>>> GetDocuments(Guid id, CancellationToken cancellationToken)
    {
        var data = await _projectService.GetDocumentsAsync(id, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<ProjectDocumentListItemDto>>.Ok(data, "Documentos obtenidos correctamente"));
    }

    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    [Authorize(Policy = "ProjectsDocumentsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDocumentListItemDto>>> UploadDocument(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] string title,
        [FromForm] string type,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponseDto<ProjectDocumentListItemDto>.Fail("Debes seleccionar un archivo"));
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var uploadedById))
        {
            return Unauthorized(ApiResponseDto<ProjectDocumentListItemDto>.Fail("Token inválido"));
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        var data = await _projectService.AddDocumentAsync(id, uploadedById, title, type, file.FileName, file.ContentType, memory.ToArray(), notes, cancellationToken);
        return Ok(ApiResponseDto<ProjectDocumentListItemDto>.Ok(data, "Documento cargado correctamente"));
    }

    [HttpPost("{id:guid}/documents/{documentId:guid}/versions")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    [Authorize(Policy = "ProjectsDocumentsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDocumentListItemDto>>> UploadDocumentVersion(
        Guid id,
        Guid documentId,
        [FromForm] IFormFile file,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponseDto<ProjectDocumentListItemDto>.Fail("Debes seleccionar un archivo"));
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var uploadedById))
        {
            return Unauthorized(ApiResponseDto<ProjectDocumentListItemDto>.Fail("Token inválido"));
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        var data = await _projectService.AddDocumentVersionAsync(id, documentId, uploadedById, file.FileName, file.ContentType, memory.ToArray(), notes, cancellationToken);
        return Ok(ApiResponseDto<ProjectDocumentListItemDto>.Ok(data, "Nueva versión cargada correctamente"));
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/versions")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<ProjectDocumentVersionDto>>>> GetDocumentVersions(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var data = await _projectService.GetDocumentVersionsAsync(id, documentId, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<ProjectDocumentVersionDto>>.Ok(data, "Versiones obtenidas correctamente"));
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var file = await _projectService.GetDocumentContentAsync(id, documentId, cancellationToken);
        if (file is null)
        {
            return NotFound(ApiResponseDto<object>.Fail("Documento no encontrado"));
        }

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "ProjectsDocumentsRead")]
    public async Task<IActionResult> DownloadDocumentVersion(Guid id, Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var file = await _projectService.GetDocumentVersionContentAsync(id, documentId, versionId, cancellationToken);
        if (file is null)
        {
            return NotFound(ApiResponseDto<object>.Fail("Versión de documento no encontrada"));
        }

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    [Authorize(Policy = "ProjectsDocumentsDelete")]
    public async Task<ActionResult<ApiResponseDto<object>>> DeleteDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var deletedById))
        {
            return Unauthorized(ApiResponseDto<object>.Fail("Token inválido"));
        }

        var deleted = await _projectService.DeleteDocumentAsync(id, documentId, deletedById, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponseDto<object>.Fail("Documento no encontrado"));
        }

        return Ok(ApiResponseDto<object>.Ok(new { }, "Documento eliminado correctamente"));
    }

}
