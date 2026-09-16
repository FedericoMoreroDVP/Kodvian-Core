namespace Kodvian.Core.Application.Tasks.Abstractions;

public record TaskAttachmentAccess(Guid UserId, Guid? DeveloperId, bool CanReadAll, bool CanWriteAll, bool CanReadOwn, bool CanWriteOwn);
public record TaskAttachmentDto(Guid Id, string FileName, string ContentType, long Size, DateTime CreatedAt, Guid UploadedById, string UploadedByName);
public record TaskAttachmentContent(byte[] Content, string ContentType, string FileName);

public interface ITaskAttachmentService
{
    Task<IReadOnlyCollection<TaskAttachmentDto>> ListAsync(Guid taskId, TaskAttachmentAccess access, CancellationToken ct);
    Task<TaskAttachmentDto> UploadAsync(Guid taskId, Guid uploadId, string fileName, byte[] content, TaskAttachmentAccess access, CancellationToken ct);
    Task<TaskAttachmentContent> DownloadAsync(Guid taskId, Guid id, TaskAttachmentAccess access, CancellationToken ct);
    Task DeleteAsync(Guid taskId, Guid id, TaskAttachmentAccess access, CancellationToken ct);
}
