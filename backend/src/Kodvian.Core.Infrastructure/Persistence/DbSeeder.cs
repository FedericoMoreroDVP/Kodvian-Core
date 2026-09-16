using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kodvian.Core.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAdminUserAsync(IServiceProvider serviceProvider, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";

        var adminEmail = (configuration["AuthSeed:AdminEmail"] ?? string.Empty).Trim().ToLowerInvariant();
        var adminPassword = configuration["AuthSeed:AdminPassword"] ?? string.Empty;
        var adminFullName = configuration["AuthSeed:AdminFullName"] ?? string.Empty;

        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            adminEmail = string.IsNullOrWhiteSpace(adminEmail) ? "admin@kodvian.local" : adminEmail;
            adminPassword = string.IsNullOrWhiteSpace(adminPassword) ? "Admin12345!" : adminPassword;
            adminFullName = string.IsNullOrWhiteSpace(adminFullName) ? "Administrador General" : adminFullName;
        }

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword) || string.IsNullOrWhiteSpace(adminFullName))
        {
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException("AuthSeed is not configured for non-development environments.");
        }

        if (string.Equals(adminPassword, "Admin123*", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("AuthSeed password is not secure.");
        }

        var exists = await dbContext.Users.AnyAsync(x => x.Email == adminEmail, cancellationToken);
        if (exists)
        {
            return;
        }

        var adminRole = await dbContext.Roles.FirstOrDefaultAsync(x => x.Name == RoleNames.Administrator, cancellationToken);
        if (adminRole is null)
        {
            return;
        }

        dbContext.Users.Add(new User
        {
            FullName = adminFullName,
            Email = adminEmail,
            PasswordHash = passwordHasher.HashPassword(adminPassword),
            UserRoles = [new UserRole { RoleId = adminRole.Id }]
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedFinancialCategoriesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        var defaultCategories = new (string Name, FinancialMovementType Type)[]
        {
            ("Abono mensual", FinancialMovementType.Ingreso),
            ("Desarrollo a medida", FinancialMovementType.Ingreso),
            ("Hosting", FinancialMovementType.Egreso),
            ("Dominio", FinancialMovementType.Egreso),
            ("Herramientas", FinancialMovementType.Egreso),
            ("Publicidad", FinancialMovementType.Egreso),
            ("Impuestos", FinancialMovementType.Egreso),
            ("Otros", FinancialMovementType.Egreso)
        };

        var existing = await dbContext.FinancialCategories
            .AsNoTracking()
            .Select(x => new { x.Name, x.MovementType })
            .ToListAsync(cancellationToken);

        foreach (var item in defaultCategories)
        {
            var alreadyExists = existing.Any(x =>
                string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase)
                && x.MovementType == item.Type);

            if (alreadyExists)
            {
                continue;
            }

            dbContext.FinancialCategories.Add(new FinancialCategory
            {
                Name = item.Name,
                MovementType = item.Type,
                IsActive = true,
                Activo = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
