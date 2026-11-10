using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

public sealed class AiToolCatalogTests
{
    [Fact]
    public async Task ClientToolOmitsContactTaxFinancialAndNotesData()
    {
        await using var db = CreateDatabase();
        db.Clients.Add(new Client
        {
            CommercialName = "Cliente visible",
            LegalName = "Razón Legal Privada",
            TaxId = "30-12345678-9",
            ContactName = "Contacto Privado",
            ContactEmail = "private@example.test",
            ContactPhone = "+54 11 5555 1111",
            Address = "Dirección privada",
            MonthlyAmount = 123456,
            Notes = "Nota confidencial"
        });
        db.SaveChanges();

        var catalog = CreateCatalog(db);
        var actor = AdminActor();
        var result = await catalog.ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace-test", "clients.list", new JsonObject(), CancellationToken.None);
        var json = result.Data.ToJsonString();

        Assert.Contains("Cliente visible", json);
        Assert.DoesNotContain("Razón Legal Privada", json);
        Assert.DoesNotContain("30-12345678-9", json);
        Assert.DoesNotContain("Contacto Privado", json);
        Assert.DoesNotContain("private@example.test", json);
        Assert.DoesNotContain("5555 1111", json);
        Assert.DoesNotContain("Dirección privada", json);
        Assert.DoesNotContain("123456", json);
        Assert.DoesNotContain("Nota confidencial", json);
    }

    [Fact]
    public async Task MeetingsWithoutDatesReturnTheNextSevenDays()
    {
        await using var db = CreateDatabase();
        var actor = AdminActor();
        var client = new Client { CommercialName = "Cliente" };
        var project = new Project { ClienteId = client.Id, Nombre = "Proyecto" };
        db.Clients.Add(client);
        db.Projects.Add(project);
        db.Users.Add(new User { Id = actor.UserId, FullName = "Admin", Email = "admin@example.test" });
        db.Meetings.AddRange(
            new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Reunion proxima", Inicio = DateTime.UtcNow.AddDays(1), Fin = DateTime.UtcNow.AddDays(1).AddHours(1) },
            new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Reunion pasada", Inicio = DateTime.UtcNow.AddDays(-10), Fin = DateTime.UtcNow.AddDays(-10).AddHours(1) },
            new Meeting { ProyectoId = project.Id, CreadoPorId = actor.UserId, Titulo = "Reunion lejana", Inicio = DateTime.UtcNow.AddDays(20), Fin = DateTime.UtcNow.AddDays(20).AddHours(1) });
        db.SaveChanges();

        var result = await CreateCatalog(db).ExecuteAsync(actor, Guid.NewGuid(), Guid.Empty, "trace", "meetings.list", new JsonObject(), CancellationToken.None);
        var json = result.Data.ToJsonString();

        Assert.Contains("Reunion proxima", json);
        Assert.DoesNotContain("Reunion pasada", json);
        Assert.DoesNotContain("Reunion lejana", json);
    }

    [Fact]
    public async Task ToolsRequireAdminAiPermissionAndCurrentSession()
    {
        await using var db = CreateDatabase();
        var catalog = CreateCatalog(db);
        var actor = AdminActor();
        Assert.Contains(catalog.GetAvailableTools(actor), x => x.Name == "finance.get_summary");

        var noAiPermission = new AiExecutionContext
        {
            UserId = actor.UserId, SessionVersion = actor.SessionVersion, Roles = actor.Roles,
            Permissions = new HashSet<string>([PermissionCodes.ClientsRead], StringComparer.Ordinal)
        };
        Assert.Empty(catalog.GetAvailableTools(noAiPermission));

        var noLongerAdmin = new AiExecutionContext
        {
            UserId = actor.UserId, SessionVersion = actor.SessionVersion,
            Roles = new HashSet<string>([RoleNames.Analyst], StringComparer.Ordinal),
            Permissions = actor.Permissions
        };
        Assert.Empty(catalog.GetAvailableTools(noLongerAdmin));
    }

    private static KodvianDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new KodvianDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiExecutionContext AdminActor() => new()
    {
        UserId = Guid.NewGuid(),
        SessionVersion = Guid.NewGuid(),
        Roles = new HashSet<string>([RoleNames.Administrator], StringComparer.Ordinal),
        Permissions = new HashSet<string>(RolePermissionMap.GetPermissions(RoleNames.Administrator), StringComparer.Ordinal)
    };

    private static AiToolCatalog CreateCatalog(KodvianDbContext db)
    {
        var currentUser = new TestCurrentUser();
        var finance = new FinanceOverviewService(db, currentUser);
        return new AiToolCatalog(
            new ClientService(db),
            new ProjectService(db, new NullFileStorage(), Options.Create(new StorageOptions())),
            new TaskService(db, new NullFileStorage(), NullLogger<TaskService>.Instance),
            new MeetingService(db),
            finance,
            new DashboardService(db, finance),
            new FakeProviderConfiguration(),
            new ActiveSessionValidator(),
            db);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? SessionVersion { get; } = Guid.NewGuid();
    }

    private sealed class ActiveSessionValidator : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeProviderConfiguration : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto
        {
            IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true
        });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class NullFileStorage : IFileStorageService
    {
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
