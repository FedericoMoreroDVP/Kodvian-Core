using System.Linq.Expressions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Developers.Abstractions;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class ProjectDeveloperAssignmentService : IProjectDeveloperAssignmentService
{
    private readonly KodvianDbContext _dbContext;

    public ProjectDeveloperAssignmentService(KodvianDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<ProjectDeveloperAssignmentDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProjectDeveloperAssignments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Activo)
            .ThenBy(x => x.Developer != null ? x.Developer.FullName : string.Empty)
            .Select(ToDto())
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectDeveloperAssignmentDto> CreateAsync(Guid projectId, ProjectDeveloperAssignmentCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        var projectExists = await _dbContext.Projects.AnyAsync(x => x.Id == projectId, cancellationToken);
        if (!projectExists)
        {
            throw new InvalidOperationException("Proyecto no encontrado");
        }

        var developerExists = await _dbContext.Developers.AnyAsync(x => x.Id == request.DeveloperId && x.Activo
            && (!x.Users.Any() || x.Users.Any(u => u.UserRoles.Any(r => r.Role.Activo && r.Role.Name == RoleNames.Developer))), cancellationToken);
        if (!developerExists)
        {
            throw new InvalidOperationException("Desarrollador no encontrado");
        }

        var existing = await _dbContext.ProjectDeveloperAssignments
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.DeveloperId == request.DeveloperId, cancellationToken);

        if (existing is not null)
        {
            existing.Activo = true;
            existing.Notes = Normalize(request.Notes);
            existing.FechaActualizacion = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(existing.Id, cancellationToken);
        }

        var entity = new ProjectDeveloperAssignment
        {
            ProjectId = projectId,
            DeveloperId = request.DeveloperId,
            Notes = Normalize(request.Notes),
            Activo = true
        };

        _dbContext.ProjectDeveloperAssignments.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ProjectDeveloperAssignments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Activo = false;
        entity.FechaActualizacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ProjectDeveloperAssignmentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.ProjectDeveloperAssignments
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto())
            .FirstAsync(cancellationToken);
    }

    private static Expression<Func<ProjectDeveloperAssignment, ProjectDeveloperAssignmentDto>> ToDto()
    {
        return x => new ProjectDeveloperAssignmentDto
        {
            Id = x.Id,
            ProjectId = x.ProjectId,
            ProjectName = x.Project != null ? x.Project.Nombre : string.Empty,
            DeveloperId = x.DeveloperId,
            DeveloperName = x.Developer != null ? x.Developer.FullName : string.Empty,
            Notes = x.Notes,
            IsActive = x.Activo,
            CreatedAt = x.FechaCreacion
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
