using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Tasks;
using Kodvian.Core.Application.Tasks.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

public class TaskAttachmentTests : IDisposable
{
    private readonly TestDb db = new(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryStorage storage = new();
    private readonly User user = new() { FullName = "Desarrollador" };
    private readonly TaskItem task = new() { Titulo = "Prueba", DeveloperId = Guid.NewGuid() };
    private TaskAttachmentService Service => new(db, storage, NullLogger<TaskAttachmentService>.Instance);
    private TaskAttachmentAccess Own => new(user.Id, task.DeveloperId, false, false, true, true);
    private TaskAttachmentAccess General => new(user.Id, null, true, true, false, false);

    public TaskAttachmentTests()
    {
        db.Users.Add(user); db.Tasks.Add(task); db.SaveChanges();
    }

    [Fact]
    public async Task OwnUploadCanBeListedDownloadedAndDeleted()
    {
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", "evidencia"u8.ToArray(), Own, default);
        Assert.Equal(user.Id, item.UploadedById);
        Assert.Equal("Desarrollador", Assert.Single(await Service.ListAsync(task.Id, Own, default)).UploadedByName);
        Assert.Equal("evidencia"u8.ToArray(), (await Service.DownloadAsync(task.Id, item.Id, Own, default)).Content);
        await Service.DeleteAsync(task.Id, item.Id, Own, default);
        Assert.Empty(await Service.ListAsync(task.Id, Own, default));
        Assert.Empty(storage.Files);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.DownloadAsync(task.Id, item.Id, Own, default));
    }

    [Fact]
    public async Task RetryWithSameUploadIdDoesNotDuplicateFile()
    {
        var uploadId = Guid.NewGuid();
        var first = await Service.UploadAsync(task.Id, uploadId, "prueba.txt", [65], Own, default);
        var retry = await Service.UploadAsync(task.Id, uploadId, "prueba.txt", [65], Own, default);
        Assert.Equal(first.Id, retry.Id);
        Assert.Single(storage.Files);
        Assert.Single(await db.TaskAttachments.ToListAsync());
    }

    [Fact]
    public async Task ForeignTaskDeniesEveryOperationBeforeStorageAccess()
    {
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], General, default);
        var other = Own with { DeveloperId = Guid.NewGuid() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ListAsync(task.Id, other, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.DownloadAsync(task.Id, item.Id, other, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], other, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.DeleteAsync(task.Id, item.Id, other, default));
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task AttachmentCannotBeDownloadedThroughAnotherTask()
    {
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], General, default);
        var another = new TaskItem { Titulo = "Otra tarea" }; db.Tasks.Add(another); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.DownloadAsync(another.Id, item.Id, General, default));
    }

    [Fact]
    public async Task ReadOnlyCanDownloadButCannotUploadOrDelete()
    {
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], General, default);
        var read = General with { CanWriteAll = false };
        Assert.Single(await Service.ListAsync(task.Id, read, default));
        await Service.DownloadAsync(task.Id, item.Id, read, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], read, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.DeleteAsync(task.Id, item.Id, read, default));
    }

    [Fact]
    public async Task DeveloperCannotDeleteAnotherAuthorsEvidenceButManagerCan()
    {
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], General, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.DeleteAsync(task.Id, item.Id, Own with { UserId = Guid.NewGuid() }, default));
        await Service.DeleteAsync(task.Id, item.Id, General, default);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task InactiveOwnTaskDeniesReadAndWrite()
    {
        task.Activo = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ListAsync(task.Id, Own, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], Own, default));
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task ReassignmentRevokesAttachmentAccess()
    {
        var actor = Own;
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], actor, default);
        task.DeveloperId = Guid.NewGuid(); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.DownloadAsync(task.Id, item.Id, actor, default));
    }

    [Fact]
    public async Task DatabaseFailureCleansUpStoredFile()
    {
        db.FailSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], Own, default));
        Assert.Empty(storage.Files);
        Assert.Empty(await db.TaskAttachments.ToListAsync());
    }

    [Fact]
    public async Task StorageFailureReturnsARecoverableErrorWithoutCreatingAttachment()
    {
        storage.FailSave = true;
        await Assert.ThrowsAsync<StorageUnavailableException>(() => Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], Own, default));
        Assert.Empty(storage.Files); Assert.Empty(await db.TaskAttachments.ToListAsync());
    }

    [Fact]
    public async Task LostCommitAcknowledgementDoesNotDeleteTheCommittedFile()
    {
        db.FailAfterSave = true;
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], Own, default);
        Assert.Single(storage.Files);
        Assert.Equal(new byte[] { 65 }, (await Service.DownloadAsync(task.Id, item.Id, Own, default)).Content);
    }

    [Fact]
    public async Task FailedStorageDeleteCanBeRetried()
    {
        var item = await Service.UploadAsync(task.Id, Guid.NewGuid(), "prueba.txt", [65], Own, default);
        storage.FailDelete = true;
        await Assert.ThrowsAsync<StorageUnavailableException>(() => Service.DeleteAsync(task.Id, item.Id, Own, default));
        Assert.Empty(await Service.ListAsync(task.Id, Own, default));
        storage.FailDelete = false;
        await Service.DeleteAsync(task.Id, item.Id, Own, default);
        Assert.Empty(storage.Files);
    }

    [Theory]
    [InlineData("evidencia.exe")]
    [InlineData("evidencia.svg")]
    [InlineData("evidencia.html")]
    [InlineData("evidencia.png")]
    [InlineData("evidencia.pdf")]
    public void RejectsUnsupportedOrDisguisedFiles(string name) =>
        Assert.Throws<ArgumentException>(() => TaskAttachmentRules.Validate(name, "not an image"u8.ToArray()));

    [Fact]
    public void SizeLimitAndSafeNameAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => TaskAttachmentRules.Validate("a.txt", []));
        Assert.Throws<ArgumentException>(() => TaskAttachmentRules.Validate("a.txt", new byte[TaskAttachmentRules.MaxBytes + 1]));
        Assert.Equal("prueba.txt", TaskAttachmentRules.Validate("../../prueba.txt", [65]).FileName);
        var maximum = Enumerable.Repeat((byte)65, TaskAttachmentRules.MaxBytes).ToArray();
        Assert.Equal("text/plain", TaskAttachmentRules.Validate("a.TXT", maximum).ContentType);
    }

    public void Dispose() => db.Dispose();
    private sealed class TestDb(DbContextOptions<KodvianDbContext> options) : KodvianDbContext(options)
    {
        public bool FailSave;
        public bool FailAfterSave;
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new InvalidOperationException("Simulated database failure");
            var result = await base.SaveChangesAsync(cancellationToken);
            if (FailAfterSave) throw new InvalidOperationException("Simulated lost acknowledgement");
            return result;
        }
    }
    private sealed class MemoryStorage : IFileStorageService
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public bool FailDelete;
        public bool FailSave;
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new IOException("Simulated storage failure");
            var key = Guid.NewGuid() + extension; Files[key] = content; return Task.FromResult(key);
        }
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Files[path]);
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (FailDelete) throw new IOException("Simulated storage failure");
            Files.Remove(path); return Task.CompletedTask;
        }
    }
}
