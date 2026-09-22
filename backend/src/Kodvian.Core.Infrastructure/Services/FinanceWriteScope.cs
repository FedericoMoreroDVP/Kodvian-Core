using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kodvian.Core.Infrastructure.Services;

public static class FinanceWriteScope
{
    public static async Task LockAsync(KodvianDbContext db, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(746291806)", ct);
    }
    public static async Task<IDbContextTransaction?> BeginAsync(KodvianDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await LockAsync(db, ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}
