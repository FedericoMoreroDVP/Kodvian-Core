using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

public sealed class AiAuditRetentionWorkerTests
{
    [Fact]
    public async Task PurgesEventsOlderThanNinetyDaysAndKeepsRecentMetadata()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KodvianDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
            db.AiAuditEvents.AddRange(
                new AiAuditEvent { ActorUserId = Guid.NewGuid(), Decision = "Allowed", ReasonCode = "test", CreatedAtUtc = DateTime.UtcNow.AddDays(-91) },
                new AiAuditEvent { ActorUserId = Guid.NewGuid(), Decision = "Allowed", ReasonCode = "test", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) });
            db.AiProviderConfigurationAuditEvents.AddRange(
                new AiProviderConfigurationAuditEvent { ActorUserId = Guid.NewGuid(), Action = "tested", Provider = "OpenAI", CreatedAtUtc = DateTime.UtcNow.AddDays(-91) },
                new AiProviderConfigurationAuditEvent { ActorUserId = Guid.NewGuid(), Action = "tested", Provider = "OpenAI", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) });
            await db.SaveChangesAsync();
        }

        var worker = new AiAuditRetentionWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiAuditRetentionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        var events = await verifyDb.AiAuditEvents.AsNoTracking().ToListAsync();
        Assert.Single(events);
        Assert.True(events[0].CreatedAtUtc > DateTime.UtcNow.AddDays(-90));
        Assert.Single(await verifyDb.AiProviderConfigurationAuditEvents.AsNoTracking().ToListAsync());
    }
}
