using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class UserAccessGuard(KodvianDbContext db, ICurrentUser currentUser)
{
    // All account/role mutations share this PostgreSQL transaction lock, including
    // deactivation from Equipo, so two administrators cannot remove each other concurrently.
    public async Task<IDbContextTransaction?> BeginAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(746291805)", ct);
            // Keep profile associations stable while Finance links existing people.
            // Lock order is always accounts, then finance.
            await FinanceWriteScope.LockAsync(db, ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public async Task<bool> CheckActorAsync(bool requireAdministrator, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == currentUser.UserId && x.Activo, ct);
        if (actor is null || actor.SessionVersion != currentUser.SessionVersion)
            throw new UnauthorizedAccessException("La sesión ya no es válida. Inicia sesión nuevamente.");
        var roles = actor.UserRoles.Where(x => x.Role.Activo).Select(x => x.Role.Name).ToArray();
        var administrator = roles.Contains(RoleNames.Administrator);
        if (requireAdministrator ? !administrator : !RolePermissionMap.GetPermissions(roles).Contains(PermissionCodes.TeamWrite))
            throw new UnauthorizedAccessException("No tienes permisos para modificar esta cuenta");
        return administrator;
    }

    public static bool IsAdministrator(User user) => user.UserRoles.Any(x => x.Role.Name == RoleNames.Administrator && x.Role.Activo);

    public static void CheckTargets(bool actorIsAdministrator, IEnumerable<User> users)
    {
        if (!actorIsAdministrator && users.Any(IsAdministrator))
            throw new UnauthorizedAccessException("Solo un administrador puede modificar una cuenta administradora");
    }

    public async Task EnsureAdministratorRemainsAsync(Guid changingUserId, bool remainsActiveAdministrator, CancellationToken ct)
    {
        if (remainsActiveAdministrator) return;
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id != changingUserId && x.Activo
                && x.UserRoles.Any(r => r.Role.Name == RoleNames.Administrator && r.Role.Activo), ct))
            throw new ArgumentException("Debe permanecer al menos un administrador activo");
    }

    public async Task EnsureProfileAsync(User user, CancellationToken ct)
    {
        if (user.DeveloperId.HasValue)
        {
            if (await db.Users.AnyAsync(x => x.Id != user.Id && x.DeveloperId == user.DeveloperId, ct))
                throw new ArgumentException("El perfil está vinculado a varias cuentas. Revisa la vinculación antes de asignar el rol.");
            await db.Entry(user).Reference(x => x.Developer).LoadAsync(ct);
            if (user.Developer is null) throw new ArgumentException("El perfil vinculado no existe");
            return;
        }
        var matches = await db.Developers.Include(x => x.Users)
            .Where(x => x.Email != null && x.Email.ToLower() == user.Email.ToLower()).Take(2).ToListAsync(ct);
        if (matches.Count > 1 || matches.Any(x => x.Users.Any(u => u.Id != user.Id)))
            throw new ArgumentException("Hay perfiles con este correo vinculados o duplicados. Revisa la vinculación antes de asignar el rol.");
        var profile = matches.SingleOrDefault();
        if (profile != null && await db.Partners.AnyAsync(p => p.UserId == user.Id, ct)
            && await db.Partners.AnyAsync(p => p.DeveloperId == profile.Id || p.UserId != user.Id && p.User != null && p.User.DeveloperId == profile.Id, ct))
            throw new ArgumentException("La cuenta y el perfil ya corresponden a socios distintos. Revisa esas vinculaciones antes de unirlos.");
        if (profile is null)
        {
            profile = new Developer { FullName = user.FullName, Email = user.Email, Activo = user.Activo };
            db.Developers.Add(profile);
        }
        user.Developer = profile;
    }
}
