using System.Security.Claims;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Tasks;
using Kodvian.Core.Application.Tasks.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks/{taskId:guid}/attachments")]
public class TaskAttachmentsController(ITaskAttachmentService service) : ControllerBase
{
    private TaskAttachmentAccess Access()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("Usuario inválido");
        Guid? developerId = Guid.TryParse(User.FindFirstValue(CustomClaimTypes.DeveloperId), out var id) ? id : null;
        bool Has(string permission) => User.HasClaim(CustomClaimTypes.Permission, permission);
        return new(userId, developerId, Has(PermissionCodes.TasksRead), Has(PermissionCodes.TasksWrite),
            Has(PermissionCodes.DeveloperWorkRead), Has(PermissionCodes.DeveloperTasksStatusWrite));
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid taskId, CancellationToken ct) =>
        Ok(ApiResponseDto<IReadOnlyCollection<TaskAttachmentDto>>.Ok(await service.ListAsync(taskId, Access(), ct), "Adjuntos obtenidos correctamente"));

    [HttpPost]
    [RequestSizeLimit(TaskAttachmentRules.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid taskId, [FromForm] IFormFile file, [FromForm] Guid uploadId, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > TaskAttachmentRules.MaxBytes)
            return BadRequest(ApiResponseDto<object>.Fail("Selecciona un archivo de hasta 10 MB"));
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        var result = await service.UploadAsync(taskId, uploadId, file.FileName, memory.ToArray(), Access(), ct);
        return Ok(ApiResponseDto<TaskAttachmentDto>.Ok(result, "Archivo adjuntado correctamente"));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid taskId, Guid id, CancellationToken ct)
    {
        var file = await service.DownloadAsync(taskId, id, Access(), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid taskId, Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(taskId, id, Access(), ct);
        return Ok(ApiResponseDto<object>.Ok(new { }, "Adjunto eliminado"));
    }
}
