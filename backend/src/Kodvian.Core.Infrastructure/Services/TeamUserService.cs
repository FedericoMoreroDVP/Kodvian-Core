using System.Linq.Expressions;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Team.Abstractions;
using Kodvian.Core.Application.Team.Dtos;
using Kodvian.Core.Application.Team.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class TeamUserService : ITeamUserService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly UserAccessGuard _access;

    public TeamUserService(KodvianDbContext dbContext, IPasswordHasher passwordHasher, UserAccessGuard access)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _access = access;
    }

    public async Task<IReadOnlyCollection<TeamUserDto>> GetAnalystsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.UserRoles.Any(r => r.Role.Name == RoleNames.Analyst && r.Role.Activo))
            .OrderByDescending(x => x.Activo)
            .ThenBy(x => x.FullName)
            .Select(ToDto())
            .ToListAsync(cancellationToken);
    }

    public async Task<TeamUserDto> CreateAnalystAsync(TeamUserUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _access.BeginAsync(cancellationToken);
        await _access.CheckActorAsync(false, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("La contraseña inicial es obligatoria");
        }

        var email = NormalizeEmail(request.Email);
        var exists = await _dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("Ya existe un usuario con ese email");
        }

        var analystRole = await _dbContext.Roles.FirstOrDefaultAsync(x => x.Name == RoleNames.Analyst && x.Activo, cancellationToken);
        if (analystRole is null)
        {
            throw new InvalidOperationException("El rol Analista no está configurado");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            UserRoles = [new UserRole { RoleId = analystRole.Id }],
            Activo = request.IsActive
        };

        await _access.EnsureProfileAsync(user, cancellationToken);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await GetAnalystByIdAsync(user.Id, cancellationToken) ?? throw new InvalidOperationException("No se pudo recuperar el usuario creado");
    }

    public async Task<TeamUserDto?> UpdateAnalystAsync(Guid id, TeamUserUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _access.BeginAsync(cancellationToken);
        var actorIsAdmin = await _access.CheckActorAsync(false, cancellationToken);
        var user = await _dbContext.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserRoles.Any(r => r.Role.Name == RoleNames.Analyst && r.Role.Activo), cancellationToken);

        if (user is null)
        {
            return null;
        }

        UserAccessGuard.CheckTargets(actorIsAdmin, [user]);
        if (user.DeveloperId.HasValue && await _dbContext.Users.AnyAsync(x => x.Id != user.Id && x.DeveloperId == user.DeveloperId, cancellationToken))
            throw new ArgumentException("El perfil está vinculado a varias cuentas. Revisa la vinculación antes de editarlo.");
        var email = NormalizeEmail(request.Email);
        var emailExists = await _dbContext.Users.AnyAsync(x => x.Id != id && x.Email == email, cancellationToken);
        if (emailExists)
        {
            throw new InvalidOperationException("Ya existe un usuario con ese email");
        }

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.Activo = request.IsActive;
        user.FechaActualizacion = DateTime.UtcNow;
        user.SessionVersion = Guid.NewGuid();
        await _access.EnsureProfileAsync(user, cancellationToken);

        if (user.Developer is not null)
        {
            user.Developer.FullName = user.FullName;
            user.Developer.Email = user.Email;
            user.Developer.Activo = user.Activo;
            user.Developer.FechaActualizacion = DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = _passwordHasher.HashPassword(request.Password);
        }

        await _access.EnsureAdministratorRemainsAsync(user.Id, user.Activo && UserAccessGuard.IsAdministrator(user), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await GetAnalystByIdAsync(id, cancellationToken);
    }

    private async Task<TeamUserDto?> GetAnalystByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == id && x.UserRoles.Any(r => r.Role.Name == RoleNames.Analyst && r.Role.Activo))
            .Select(ToDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Expression<Func<User, TeamUserDto>> ToDto()
    {
        return x => new TeamUserDto
        {
            Id = x.Id,
            FullName = x.FullName,
            Email = x.Email,
            Roles = x.UserRoles.Where(r => r.Role.Activo).OrderBy(r => r.Role.Name).Select(r => r.Role.Name).ToArray(),
            DeveloperId = x.DeveloperId,
            IsActive = x.Activo,
            CreatedAt = x.FechaCreacion,
            UpdatedAt = x.FechaActualizacion
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

}
