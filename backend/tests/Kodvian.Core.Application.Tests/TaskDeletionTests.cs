using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DomainTaskStatus = Kodvian.Core.Domain.Enums.TaskStatus;

namespace Kodvian.Core.Application.Tests;

public class TaskDeletionTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryStorage storage = new();
    private TaskService Service => new(db, storage, NullLogger<TaskService>.Instance);

    [Fact]
    public async Task CancelledTaskAndItsAttachmentsArePermanentlyDeleted()
    {
        var task = new TaskItem { Titulo = "Cancelar", Estado = DomainTaskStatus.Cancelada };
        db.Tasks.Add(task);
        db.TaskAttachments.AddRange(
            new TaskAttachment { TaskId = task.Id, StoragePath = "2026/09/uno.png", FileName = "uno.png", ContentType = "image/png" },
            new TaskAttachment { TaskId = task.Id, StoragePath = "2026/09/dos.pdf", FileName = "dos.pdf", ContentType = "application/pdf" });
        storage.Files.UnionWith(["2026/09/uno.png", "2026/09/dos.pdf"]);
        await db.SaveChangesAsync();

        var deleted = await Service.DeleteAsync(task.Id);

        Assert.True(deleted);
        Assert.Empty(await db.Tasks.ToListAsync());
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task ActiveTaskCannotBeDeletedAndKeepsItsAttachment()
    {
        var task = new TaskItem { Titulo = "Activa", Estado = DomainTaskStatus.EnCurso };
        db.Tasks.Add(task);
        db.TaskAttachments.Add(new TaskAttachment { TaskId = task.Id, StoragePath = "2026/09/evidencia.png", FileName = "evidencia.png", ContentType = "image/png" });
        storage.Files.Add("2026/09/evidencia.png");
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service.DeleteAsync(task.Id));

        Assert.Single(await db.Tasks.ToListAsync());
        Assert.Single(await db.TaskAttachments.ToListAsync());
        Assert.Contains("2026/09/evidencia.png", storage.Files);
    }

    [Fact]
    public async Task StorageFailureKeepsCancelledTaskAndAttachmentsForRetry()
    {
        var task = new TaskItem { Titulo = "Reintentar", Estado = DomainTaskStatus.Cancelada };
        db.Tasks.Add(task);
        db.TaskAttachments.Add(new TaskAttachment { TaskId = task.Id, StoragePath = "2026/09/reintentar.png", FileName = "reintentar.png", ContentType = "image/png" });
        storage.Files.Add("2026/09/reintentar.png");
        storage.FailDelete = true;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<StorageUnavailableException>(() => Service.DeleteAsync(task.Id));

        Assert.Single(await db.Tasks.ToListAsync());
        Assert.Single(await db.TaskAttachments.ToListAsync());
        Assert.Contains("2026/09/reintentar.png", storage.Files);
    }

    [Fact]
    public async Task MissingTaskIsNotDeleted() => Assert.False(await Service.DeleteAsync(Guid.NewGuid()));

    public void Dispose() => db.Dispose();

    private sealed class MemoryStorage : IFileStorageService
    {
        public HashSet<string> Files { get; } = [];
        public bool FailDelete { get; set; }
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            if (FailDelete) throw new IOException("Simulated storage failure");
            Files.Remove(storagePath);
            return Task.CompletedTask;
        }
    }
}
