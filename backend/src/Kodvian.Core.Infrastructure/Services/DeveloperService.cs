using Kodvian.Core.Application.Developers.Abstractions;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class DeveloperService : IDeveloperService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly UserAccessGuard _access;

    public DeveloperService(KodvianDbContext dbContext, IPasswordHasher passwordHasher, UserAccessGuard access)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _access = access;
    }

    public async Task<IReadOnlyCollection<DeveloperDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Developers
            .AsNoTracking()
            .Where(x => !x.Users.Any() || x.Users.Any(u => u.UserRoles.Any(r => r.Role.Name == RoleNames.Developer && r.Role.Activo)))
            .OrderBy(x => x.FullName)
            .Select(x => new DeveloperDto
            {
                Id = x.Id,
                FullName = x.FullName,
                Email = x.Email,
                Phone = x.Phone,
                TaxId = x.TaxId,
                Notes = x.Notes,
                IsActive = x.Activo,
                HasSystemAccess = x.Users.Any(),
                IsSystemAccessActive = x.Users.Any(u => u.Activo),
                Roles = x.Users.SelectMany(u => u.UserRoles).Where(r => r.Role.Activo).Select(r => r.Role.Name).Distinct().ToArray()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DeveloperDto> CreateAsync(DeveloperUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _access.BeginAsync(cancellationToken);
        await _access.CheckActorAsync(false, cancellationToken);
        var developer = new Developer();
        ApplyRequest(developer, request);
        _dbContext.Developers.Add(developer);
        await ApplySystemAccessAsync(developer, request, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await GetDtoAsync(developer.Id, cancellationToken);
    }

    public async Task<DeveloperDto?> UpdateAsync(Guid id, DeveloperUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _access.BeginAsync(cancellationToken);
        var isAdmin = await _access.CheckActorAsync(false, cancellationToken);
        var linkedUsers = await _dbContext.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Where(x => x.DeveloperId == id).ToListAsync(cancellationToken);
        UserAccessGuard.CheckTargets(isAdmin, linkedUsers);
        if (linkedUsers.Count > 1) throw new ArgumentException("El perfil está vinculado a varias cuentas. Revisa la vinculación antes de editarlo.");
        var developer = await _dbContext.Developers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (developer is null)
        {
            return null;
        }

        ApplyRequest(developer, request);
        await ApplySystemAccessAsync(developer, request, cancellationToken);
        developer.FechaActualizacion = DateTime.UtcNow;
        if (linkedUsers.Count == 1)
            await _access.EnsureAdministratorRemainsAsync(linkedUsers[0].Id, linkedUsers[0].Activo && UserAccessGuard.IsAdministrator(linkedUsers[0]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return await GetDtoAsync(developer.Id, cancellationToken);
    }

    private static void ApplyRequest(Developer target, DeveloperUpsertRequestDto request)
    {
        target.FullName = request.FullName.Trim();
        target.Email = Normalize(request.Email);
        target.Phone = Normalize(request.Phone);
        target.TaxId = Normalize(request.TaxId);
        target.Notes = Normalize(request.Notes);
        target.Activo = request.IsActive;
    }

    private async Task ApplySystemAccessAsync(Developer developer, DeveloperUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var existingUser = await _dbContext.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.DeveloperId == developer.Id, cancellationToken);

        if (existingUser is null && !request.EnableSystemAccess) return;

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new InvalidOperationException("El email es obligatorio para una persona con cuenta de usuario.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailOwnerExists = await _dbContext.Users.AnyAsync(x => x.Email == normalizedEmail && x.DeveloperId != developer.Id, cancellationToken);
        if (emailOwnerExists)
        {
            throw new InvalidOperationException("Ya existe un usuario con ese email.");
        }

        developer.Email = normalizedEmail;

        if (existingUser is null)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(x => x.Name == RoleNames.Developer && x.Activo, cancellationToken)
                ?? throw new InvalidOperationException("El rol Desarrollador no está configurado.");
            if (string.IsNullOrWhiteSpace(request.AccessPassword))
            {
                throw new InvalidOperationException("La contraseña inicial es obligatoria para crear el acceso.");
            }

            _dbContext.Users.Add(new User
            {
                FullName = developer.FullName,
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.HashPassword(request.AccessPassword),
                UserRoles = [new UserRole { RoleId = role.Id }],
                DeveloperId = developer.Id,
                Activo = request.IsSystemAccessActive && request.IsActive
            });
            return;
        }

        existingUser.FullName = developer.FullName;
        existingUser.Email = normalizedEmail;
        existingUser.Activo = request.EnableSystemAccess && request.IsSystemAccessActive && request.IsActive;
        existingUser.SessionVersion = Guid.NewGuid();
        existingUser.FechaActualizacion = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.AccessPassword))
        {
            existingUser.PasswordHash = _passwordHasher.HashPassword(request.AccessPassword);
        }
    }

    private async Task<DeveloperDto> GetDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Developers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new DeveloperDto
            {
                Id = x.Id,
                FullName = x.FullName,
                Email = x.Email,
                Phone = x.Phone,
                TaxId = x.TaxId,
                Notes = x.Notes,
                IsActive = x.Activo,
                HasSystemAccess = x.Users.Any(),
                IsSystemAccessActive = x.Users.Any(u => u.Activo),
                Roles = x.Users.SelectMany(u => u.UserRoles).Where(r => r.Role.Activo).Select(r => r.Role.Name).Distinct().ToArray()
            })
            .FirstAsync(cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static DeveloperDto ToDto(Developer x)
    {
        return new DeveloperDto
        {
            Id = x.Id,
            FullName = x.FullName,
            Email = x.Email,
            Phone = x.Phone,
            TaxId = x.TaxId,
            Notes = x.Notes,
            IsActive = x.Activo
        };
    }
}
