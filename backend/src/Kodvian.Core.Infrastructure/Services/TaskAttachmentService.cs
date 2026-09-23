using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Tasks;
using Kodvian.Core.Application.Tasks.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kodvian.Core.Infrastructure.Services;

public class TaskAttachmentService(KodvianDbContext db, IFileStorageService storage, ILogger<TaskAttachmentService> logger) : ITaskAttachmentService
{
    private async Task CheckAccess(Guid taskId, TaskAttachmentAccess access, bool write, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().Where(t => t.Id == taskId)
            .Select(t => new { t.DeveloperId, t.Activo }).SingleOrDefaultAsync(ct);
        if (task is null) throw new KeyNotFoundException("Tarea no encontrada");
        var general = write ? access.CanWriteAll && access.CanReadAll : access.CanReadAll;
        var own = access.CanReadOwn && (!write || access.CanWriteOwn) && access.DeveloperId.HasValue
            && task.DeveloperId == access.DeveloperId && task.Activo;
        if (!general && !own) throw new UnauthorizedAccessException("No tienes permiso para acceder a los adjuntos de esta tarea");
    }

    public async Task<IReadOnlyCollection<TaskAttachmentDto>> ListAsync(Guid taskId, TaskAttachmentAccess access, CancellationToken ct)
    {
        await CheckAccess(taskId, access, false, ct);
        return await db.TaskAttachments.AsNoTracking().Where(a => a.TaskId == taskId && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt).Select(a => new TaskAttachmentDto(a.Id, a.FileName, a.ContentType, a.Size, a.CreatedAt, a.UploadedById, a.UploadedBy.FullName)).ToListAsync(ct);
    }

    public async Task<TaskAttachmentDto> UploadAsync(Guid taskId, Guid uploadId, string fileName, byte[] content, TaskAttachmentAccess access, CancellationToken ct)
    {
        await CheckAccess(taskId, access, true, ct);
        if (uploadId == Guid.Empty) throw new ArgumentException("Identificador de subida inválido");
        var validated = TaskAttachmentRules.Validate(fileName, content);
        var existing = await db.TaskAttachments.Include(a => a.UploadedBy).SingleOrDefaultAsync(a => a.TaskId == taskId && a.UploadId == uploadId, ct);
        if (existing is not null)
        {
            if (existing.UploadedById != access.UserId || existing.IsDeleted) throw new ArgumentException("La subida ya no está disponible");
            return Map(existing);
        }
        var author = await db.Users.SingleOrDefaultAsync(u => u.Id == access.UserId && u.Activo, ct)
            ?? throw new UnauthorizedAccessException("Usuario no disponible");
        var path = await SaveFileAsync(content, validated.Extension, ct);
        var attachment = new TaskAttachment { TaskId = taskId, UploadId = uploadId, UploadedById = access.UserId,
            UploadedBy = author, FileName = validated.FileName, ContentType = validated.ContentType, Size = content.LongLength, StoragePath = path };
        db.TaskAttachments.Add(attachment);
        try { await db.SaveChangesAsync(ct); }
        catch
        {
            db.Entry(attachment).State = EntityState.Detached;
            // A lost commit acknowledgement may still have persisted this very file.
            // Check before cleanup; never remove an object referenced by a committed row.
            TaskAttachment? duplicate;
            try
            {
                duplicate = await db.TaskAttachments.Include(a => a.UploadedBy)
                    .SingleOrDefaultAsync(a => a.TaskId == taskId && a.UploadId == uploadId, CancellationToken.None);
            }
            catch (Exception lookup)
            {
                logger.LogError(lookup, "No se pudo verificar la subida {UploadId}; conservar archivo para reconciliación: {StoragePath}", uploadId, path);
                throw;
            }
            if (duplicate?.StoragePath == path && !duplicate.IsDeleted && duplicate.UploadedById == access.UserId)
                return Map(duplicate);
            try { await DeleteFileAsync(path, CancellationToken.None); }
            catch (Exception cleanup) { logger.LogError(cleanup, "No se pudo limpiar el archivo de una subida fallida: {StoragePath}", path); }
            // A concurrent retry may have committed the same logical upload.
            if (duplicate is not null && !duplicate.IsDeleted && duplicate.UploadedById == access.UserId) return Map(duplicate);
            throw;
        }
        return Map(attachment);
    }

    public async Task<TaskAttachmentContent> DownloadAsync(Guid taskId, Guid id, TaskAttachmentAccess access, CancellationToken ct)
    {
        await CheckAccess(taskId, access, false, ct);
        var a = await db.TaskAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.TaskId == taskId && !a.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Adjunto no encontrado");
        return new(await ReadFileAsync(a.StoragePath, ct), a.ContentType, a.FileName);
    }

    public async Task DeleteAsync(Guid taskId, Guid id, TaskAttachmentAccess access, CancellationToken ct)
    {
        await CheckAccess(taskId, access, true, ct);
        var a = await db.TaskAttachments.SingleOrDefaultAsync(a => a.Id == id && a.TaskId == taskId, ct)
            ?? throw new KeyNotFoundException("Adjunto no encontrado");
        if (!(access.CanWriteAll && access.CanReadAll) && a.UploadedById != access.UserId)
            throw new UnauthorizedAccessException("Solo puedes eliminar los archivos que subiste");
        // Keep the tombstone and path so a failed storage deletion can be retried safely.
        a.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        await DeleteFileAsync(a.StoragePath, ct);
    }

    private async Task<string> SaveFileAsync(byte[] content, string extension, CancellationToken ct)
    {
        try { return await storage.SaveAsync(content, extension, ct); }
        catch (StorageUnavailableException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException and not ArgumentException)
        {
            logger.LogError(exception, "No se pudo guardar una evidencia de tarea");
            throw new StorageUnavailableException(exception);
        }
    }

    private async Task<byte[]> ReadFileAsync(string path, CancellationToken ct)
    {
        try { return await storage.ReadAsync(path, ct); }
        catch (FileNotFoundException) { throw; }
        catch (StorageUnavailableException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException and not ArgumentException)
        {
            logger.LogError(exception, "No se pudo leer una evidencia de tarea");
            throw new StorageUnavailableException(exception);
        }
    }

    private async Task DeleteFileAsync(string path, CancellationToken ct)
    {
        try { await storage.DeleteAsync(path, ct); }
        catch (StorageUnavailableException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException and not ArgumentException)
        {
            logger.LogError(exception, "No se pudo eliminar una evidencia de tarea");
            throw new StorageUnavailableException(exception);
        }
    }

    private static TaskAttachmentDto Map(TaskAttachment a) => new(a.Id, a.FileName, a.ContentType, a.Size, a.CreatedAt, a.UploadedById, a.UploadedBy.FullName);
}
