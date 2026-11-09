using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class AiAuditRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AiAuditRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "No se pudo purgar la auditoría de Oficina Inteligente vencida.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        var cutoff = DateTime.UtcNow.Subtract(Retention);
        int deleted;
        int deletedConfigurationEvents;
        if (dbContext.Database.IsRelational())
        {
            deleted = await dbContext.AiAuditEvents.Where(x => x.CreatedAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
            deletedConfigurationEvents = await dbContext.AiProviderConfigurationAuditEvents.Where(x => x.CreatedAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var expiredEvents = await dbContext.AiAuditEvents.Where(x => x.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
            var expiredConfigurationEvents = await dbContext.AiProviderConfigurationAuditEvents.Where(x => x.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
            deleted = expiredEvents.Count;
            deletedConfigurationEvents = expiredConfigurationEvents.Count;
            dbContext.AiAuditEvents.RemoveRange(expiredEvents);
            dbContext.AiProviderConfigurationAuditEvents.RemoveRange(expiredConfigurationEvents);
            if (deleted + deletedConfigurationEvents > 0) await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (deleted + deletedConfigurationEvents > 0)
            logger.LogInformation("Se purgaron {AuditCount} eventos de IA vencidos.", deleted + deletedConfigurationEvents);
    }
}
