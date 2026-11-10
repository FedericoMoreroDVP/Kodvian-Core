using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class AiPreferencesService(KodvianDbContext dbContext, ISessionValidator sessionValidator) : IAiPreferencesService
{
    public async Task<AiUserPreferencesDto> GetAsync(AiExecutionContext actor, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        return await LoadAsync(dbContext, actor.UserId, cancellationToken);
    }

    public async Task<AiUserPreferencesDto> UpdateAsync(AiExecutionContext actor, AiUserPreferencesDto request, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(actor, cancellationToken);
        var normalized = AiPreferenceRules.Normalize(request);
        await SaveAsync(dbContext, actor.UserId, normalized, cancellationToken);
        return await LoadAsync(dbContext, actor.UserId, cancellationToken);
    }

    internal static async Task<AiUserPreferencesDto> LoadAsync(KodvianDbContext db, Guid userId, CancellationToken ct)
    {
        var entity = await db.AiUserPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return entity is null ? new AiUserPreferencesDto() : new AiUserPreferencesDto
        {
            PreferredCurrency = entity.PreferredCurrency, DefaultMeetingMinutes = entity.DefaultMeetingMinutes, DefaultTaskPriority = entity.DefaultTaskPriority,
            ResponseStyle = entity.ResponseStyle, DefaultScope = entity.DefaultScope, Notes = entity.Notes, UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }

    internal static async Task SaveAsync(KodvianDbContext db, Guid userId, AiUserPreferencesDto normalized, CancellationToken ct)
    {
        var entity = await db.AiUserPreferences.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (entity is null)
        {
            entity = new AiUserPreference { UserId = userId };
            db.AiUserPreferences.Add(entity);
        }
        entity.PreferredCurrency = normalized.PreferredCurrency;
        entity.DefaultMeetingMinutes = normalized.DefaultMeetingMinutes;
        entity.DefaultTaskPriority = normalized.DefaultTaskPriority;
        entity.ResponseStyle = normalized.ResponseStyle;
        entity.DefaultScope = normalized.DefaultScope;
        entity.Notes = normalized.Notes;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureActorAsync(AiExecutionContext actor, CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(PermissionCodes.AiOfficeUse)) throw new UnauthorizedAccessException("No tienes acceso a Oficina Inteligente.");
        if (!await sessionValidator.IsValidAsync(actor.UserId, actor.SessionVersion, cancellationToken))
            throw new UnauthorizedAccessException("La sesión dejó de estar vigente.");
    }
}
