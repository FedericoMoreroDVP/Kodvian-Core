using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Auth.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthService(KodvianDbContext dbContext, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.Activo || !user.UserRoles.Any(x => x.Role.Activo))
        {
            return null;
        }

        var validPassword = _passwordHasher.VerifyPassword(user.PasswordHash, request.Password);
        if (!validPassword)
        {
            return null;
        }

        var roles = user.UserRoles.Where(x => x.Role.Activo).Select(x => x.Role.Name).OrderBy(x => x).ToArray();
        var permissions = RolePermissionMap.GetPermissions(roles);
        var token = _tokenService.GenerateToken(new TokenGenerationDto
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Roles = roles,
            SessionVersion = user.SessionVersion,
            DeveloperId = user.DeveloperId,
            Permissions = permissions
        });

        return new LoginResponseDto
        {
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            User = new CurrentUserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Roles = roles,
                DeveloperId = user.DeveloperId,
                Permissions = permissions
            }
        };
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == userId && x.Activo, cancellationToken);

        if (user is null || !user.UserRoles.Any(x => x.Role.Activo))
        {
            return null;
        }

        return new CurrentUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Roles = user.UserRoles.Where(x => x.Role.Activo).Select(x => x.Role.Name).OrderBy(x => x).ToArray(),
            DeveloperId = user.DeveloperId,
            Permissions = RolePermissionMap.GetPermissions(user.UserRoles.Where(x => x.Role.Activo).Select(x => x.Role.Name))
        };
    }
}
