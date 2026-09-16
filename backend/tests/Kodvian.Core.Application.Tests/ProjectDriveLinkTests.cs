using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Projects;
using Kodvian.Core.Application.Projects.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

public class ProjectDriveLinkTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private ProjectService Service => new(db, new UnusedStorage(), Options.Create(new StorageOptions()));

    [Theory]
    [InlineData("https://drive.google.com/drive/folders/abc?usp=sharing&resourcekey=0-key")]
    [InlineData("https://drive.google.com/drive/u/0/folders/abc")]
    [InlineData("https://drive.google.com/open?id=abc&resourcekey=key")]
    [InlineData("https://drive.google.com/file/d/abc/view?usp=sharing")]
    public void AcceptsSharedLinksAndPreservesParameters(string link)
    {
        Assert.Equal(link, GoogleDriveLinkRules.Normalize("  " + link + "  "));
    }

    [Theory]
    [InlineData("http://drive.google.com/drive/folders/abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://drive.google.com.ejemplo.com/drive/folders/abc")]
    [InlineData("https://drive.google.com@ejemplo.com/drive/folders/abc")]
    [InlineData("https://user@drive.google.com/drive/folders/abc")]
    [InlineData("https://drive.google.com:8443/drive/folders/abc")]
    [InlineData("https://drive.google.com/")]
    [InlineData("https://drive.google.com/drive/fol ders/abc")]
    [InlineData("https://drive.google.com/drive/\nfolders/abc")]
    [InlineData("https://drive.google.com\\@ejemplo.com/folders/abc")]
    public void RejectsInvalidLinks(string link) => Assert.Throws<ArgumentException>(() => GoogleDriveLinkRules.Normalize(link));

    [Fact]
    public void EnforcesLengthAndAllowsClearing()
    {
        Assert.Null(GoogleDriveLinkRules.Normalize(null));
        Assert.Null(GoogleDriveLinkRules.Normalize("  "));
        Assert.Throws<ArgumentException>(() => GoogleDriveLinkRules.Normalize("https://drive.google.com/" + new string('a', 2048)));
    }

    [Fact]
    public async Task SavesReadsReplacesAndClearsOnlyTheLink()
    {
        var project = new Project { Nombre = "Proyecto", Descripcion = "Descripción", Presupuesto = 1000, PorcentajeAvance = 40 };
        db.Projects.Add(project); await db.SaveChangesAsync();
        Assert.Null((await Service.GetDriveLinkAsync(project.Id))!.GoogleDriveFolderUrl);
        const string link = "https://drive.google.com/drive/folders/abc?resourcekey=key";
        await Service.UpdateDriveLinkAsync(project.Id, new() { GoogleDriveFolderUrl = "  " + link + " " });
        db.ChangeTracker.Clear();
        Assert.Equal(link, (await Service.GetDriveLinkAsync(project.Id))!.GoogleDriveFolderUrl);
        var saved = await db.Projects.SingleAsync();
        Assert.Equal("Proyecto", saved.Nombre);
        Assert.Equal("Descripción", saved.Descripcion);
        Assert.Equal(1000, saved.Presupuesto);
        Assert.Equal(40, saved.PorcentajeAvance);
        Assert.NotNull(saved.FechaActualizacion);
        await Service.UpdateDriveLinkAsync(project.Id, new() { GoogleDriveFolderUrl = "https://drive.google.com/drive/folders/otra" });
        Assert.EndsWith("/otra", (await Service.GetDriveLinkAsync(project.Id))!.GoogleDriveFolderUrl);
        await Service.UpdateDriveLinkAsync(project.Id, new() { GoogleDriveFolderUrl = null });
        Assert.Null((await Service.GetDriveLinkAsync(project.Id))!.GoogleDriveFolderUrl);
    }

    [Fact]
    public async Task GeneralProjectUpdatePreservesTheLink()
    {
        var client = new Client { CommercialName = "Cliente" };
        var project = new Project { Cliente = client, Nombre = "Antes", GoogleDriveFolderUrl = "https://drive.google.com/drive/folders/abc" };
        db.Projects.Add(project); await db.SaveChangesAsync();
        await Service.UpdateAsync(project.Id, new ProjectUpsertRequestDto { ClientId = client.Id, Name = "Después" });
        db.ChangeTracker.Clear();
        Assert.Equal("Después", (await Service.GetByIdAsync(project.Id))!.Name);
        Assert.Equal("https://drive.google.com/drive/folders/abc", (await Service.GetDriveLinkAsync(project.Id))!.GoogleDriveFolderUrl);
    }

    [Fact]
    public async Task MissingProjectReturnsNullAndInvalidLinkDoesNotChangeSavedValue()
    {
        Assert.Null(await Service.GetDriveLinkAsync(Guid.NewGuid()));
        Assert.Null(await Service.UpdateDriveLinkAsync(Guid.NewGuid(), new()));
        var project = new Project { Nombre = "Proyecto", GoogleDriveFolderUrl = "https://drive.google.com/drive/folders/abc" };
        db.Projects.Add(project); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service.UpdateDriveLinkAsync(project.Id, new() { GoogleDriveFolderUrl = "https://ejemplo.com" }));
        Assert.Equal("https://drive.google.com/drive/folders/abc", (await Service.GetDriveLinkAsync(project.Id))!.GoogleDriveFolderUrl);
    }

    public void Dispose() => db.Dispose();

    private sealed class UnusedStorage : IFileStorageService
    {
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drive links must not upload files");
        public Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drive links must not read files");
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drive links must not delete files");
    }
}
