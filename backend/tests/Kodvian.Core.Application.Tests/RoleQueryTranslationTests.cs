using System.Data.Common;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kodvian.Core.Application.Tests;

// Exercise the real Npgsql query translator without connecting to a database.
// InMemory alone cannot detect unsupported correlated collections in role projections.
public class RoleQueryTranslationTests
{
    [Fact]
    public async Task DeveloperAndAnalystRoleListsTranslateToPostgreSql()
    {
        var options = new DbContextOptionsBuilder<KodvianDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=test")
            .AddInterceptors(new NoConnection(), new CaptureCommand()).Options;
        await using var db = new KodvianDbContext(options);
        var guard = new UserAccessGuard(db, new NoActor());
        var dev = await Assert.ThrowsAsync<TranslatedQuery>(() => new DeveloperService(db, new NoPasswords(), guard).GetAllAsync());
        Assert.Contains("UserRoles", dev.Message);
        var analyst = await Assert.ThrowsAsync<TranslatedQuery>(() => new TeamUserService(db, new NoPasswords(), guard).GetAnalystsAsync());
        Assert.Contains("UserRoles", analyst.Message);
        var partners = await Assert.ThrowsAsync<TranslatedQuery>(() => new PartnerDirectoryService(db).ListAsync(default));
        Assert.Contains("Developers", partners.Message); Assert.Contains("Users", partners.Message);
    }

    private sealed class TranslatedQuery(string sql) : Exception(sql);
    private sealed class NoConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class CaptureCommand : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => throw new TranslatedQuery(command.CommandText);
    }
    private sealed class NoActor : ICurrentUser { public Guid? UserId => null; public Guid? SessionVersion => null; }
    private sealed class NoPasswords : IPasswordHasher
    {
        public string HashPassword(string value) => throw new NotSupportedException();
        public bool VerifyPassword(string hash, string value) => throw new NotSupportedException();
    }
}
