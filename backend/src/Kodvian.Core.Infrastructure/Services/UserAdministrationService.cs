using System.Linq.Expressions;
using Kodvian.Core.Application.Administration;
using Kodvian.Core.Application.Administration.Dtos;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class UserAdministrationService(KodvianDbContext db, UserAccessGuard access) : IUserAdministrationService
{
    public async Task<PagedResultDto<UserListItemDto>> GetAsync(UserListRequestDto request, CancellationToken ct)
    {
        await access.CheckActorAsync(true, ct);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.Email.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        return new PagedResultDto<UserListItemDto>
        {
            Items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
                .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).Select(Projection()).ToListAsync(ct),
            TotalCount = total, PageNumber = request.PageNumber, PageSize = request.PageSize
        };
    }

    public async Task<UserListItemDto?> UpdateRolesAsync(Guid id, UserRolesUpdateRequestDto request, CancellationToken ct)
    {
        await using var transaction = await access.BeginAsync(ct);
        await access.CheckActorAsync(true, ct);
        var names = RolePermissionMap.ValidateRoles(request.Roles);
        var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return null;
        if (user.SessionVersion != request.ExpectedVersion)
            throw new ArgumentException("La cuenta cambió desde que se abrió. Actualiza el listado y vuelve a intentarlo.");
        var roles = await db.Roles.Where(x => x.Activo && names.Contains(x.Name)).ToListAsync(ct);
        if (roles.Count != names.Length) throw new ArgumentException("Uno de los roles no está disponible");
        var current = user.UserRoles.Select(x => x.Role.Name).ToHashSet();
        if (current.SetEquals(names)) return await db.Users.Where(x => x.Id == id).Select(Projection()).SingleAsync(ct);
        await access.EnsureAdministratorRemainsAsync(user.Id, user.Activo && names.Contains(RoleNames.Administrator), ct);
        if (names.Contains(RoleNames.Analyst) || names.Contains(RoleNames.Developer))
            await access.EnsureProfileAsync(user, ct);
        foreach (var old in user.UserRoles.Where(x => !names.Contains(x.Role.Name)).ToList())
        {
            user.UserRoles.Remove(old);
            db.UserRoles.Remove(old);
        }
        foreach (var role in roles.Where(r => !user.UserRoles.Any(x => x.RoleId == r.Id)))
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role });
        user.SessionVersion = Guid.NewGuid();
        user.FechaActualizacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await db.Users.Where(x => x.Id == id).Select(Projection()).SingleAsync(ct);
    }

    private static Expression<Func<User, UserListItemDto>> Projection() => x => new UserListItemDto
    {
        Id = x.Id, FullName = x.FullName, Email = x.Email, IsActive = x.Activo,
        DeveloperId = x.DeveloperId, SessionVersion = x.SessionVersion,
        Roles = x.UserRoles.Where(r => r.Role.Activo).OrderBy(r => r.Role.Name).Select(r => r.Role.Name).ToArray()
    };
}
