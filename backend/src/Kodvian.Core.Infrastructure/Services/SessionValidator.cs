using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class SessionValidator(KodvianDbContext db) : ISessionValidator
{
    public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.Activo && x.SessionVersion == sessionVersion
            && x.UserRoles.Any(r => r.Role.Activo), ct);
}
