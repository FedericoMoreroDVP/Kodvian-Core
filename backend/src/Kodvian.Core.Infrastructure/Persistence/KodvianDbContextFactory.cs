using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kodvian.Core.Infrastructure.Persistence;

// Tooling only: scaffolding migrations must not run API startup/seeding.
public sealed class KodvianDbContextFactory : IDesignTimeDbContextFactory<KodvianDbContext>
{
    public KodvianDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<KodvianDbContext>()
            .UseNpgsql("Host=localhost;Database=kodvian_design;Username=postgres")
            .Options);
}
