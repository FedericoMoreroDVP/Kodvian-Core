using System.Linq.Expressions;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Tasks.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Tasks.Dtos;
using Kodvian.Core.Application.Tasks.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainTaskStatus = Kodvian.Core.Domain.Enums.TaskStatus;

namespace Kodvian.Core.Infrastructure.Services;

public class TaskService : ITaskService
{
    private readonly KodvianDbContext _dbContext;

    public TaskService(KodvianDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResultDto<TaskListItemDto>> GetPagedAsync(TaskListRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(request);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(x => x.Estado)
            .ThenBy(x => x.OrdenKanban)
            .ThenByDescending(x => x.FechaCreacion)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                ProjectId = x.ProyectoId,
                ProjectName = x.Proyecto != null ? x.Proyecto.Nombre : string.Empty,
                Title = x.Titulo,
                x.DeveloperId,
                DeveloperName = x.Developer != null ? x.Developer.FullName : x.Responsable != null ? x.Responsable.FullName : null,
                ResponsibleId = x.ResponsableId,
                ResponsibleName = x.Responsable != null ? x.Responsable.FullName : null,
                Status = x.Estado,
                Priority = x.Prioridad,
                DueDate = x.FechaVencimiento,
                EstimatedHours = x.HorasEstimadas,
                RealHours = x.HorasReales,
                KanbanOrder = x.OrdenKanban,
                IsActive = x.Activo
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(x => new TaskListItemDto
            {
                Id = x.Id,
                ProjectId = x.ProjectId,
                ProjectName = x.ProjectName,
                Title = x.Title,
                DeveloperId = x.DeveloperId,
                DeveloperName = x.DeveloperName,
                ResponsibleId = x.ResponsibleId,
                ResponsibleName = x.ResponsibleName,
                Status = x.Status.ToString(),
                Priority = x.Priority.ToString(),
                DueDate = x.DueDate,
                EstimatedHours = x.EstimatedHours,
                RealHours = x.RealHours,
                KanbanOrder = x.KanbanOrder,
                IsActive = x.IsActive
            })
            .ToList();

        return new PagedResultDto<TaskListItemDto>
        {
            Items = items,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<TaskDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Tasks
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailProjection())
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : MapToDetailDto(row);
    }

    public async Task<TaskDetailDto> CreateAsync(Guid createdById, TaskUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(createdById, request, cancellationToken);

        var task = new TaskItem
        {
            CreadoPorId = createdById
        };

        ApplyRequest(task, request);

        _dbContext.Tasks.Add(task);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var row = await _dbContext.Tasks
            .AsNoTracking()
            .Where(x => x.Id == task.Id)
            .Select(ToDetailProjection())
            .FirstAsync(cancellationToken);

        return MapToDetailDto(row);
    }

    public async Task<TaskDetailDto?> UpdateAsync(Guid id, TaskUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.Tasks.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        await ValidateReferencesAsync(null, request, cancellationToken, task.DeveloperId);
        ApplyRequest(task, request);
        task.FechaActualizacion = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var row = await _dbContext.Tasks
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailProjection())
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : MapToDetailDto(row);
    }

    public async Task<TaskDetailDto?> UpdateStatusAsync(Guid id, TaskStatusUpdateRequestDto request, CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.Tasks.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        task.Estado = ParseStatus(request.Status);
        task.OrdenKanban = request.KanbanOrder;
        task.FechaActualizacion = DateTime.UtcNow;
        if (task.Estado == DomainTaskStatus.Finalizada)
        {
            task.FechaFinalizacion ??= DateOnly.FromDateTime(DateTime.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var row = await _dbContext.Tasks
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailProjection())
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : MapToDetailDto(row);
    }

    public async Task<IReadOnlyCollection<TaskKanbanColumnDto>> GetKanbanAsync(TaskListRequestDto request, CancellationToken cancellationToken = default)
    {
        var items = await BuildFilteredQuery(request)
            .OrderBy(x => x.Estado)
            .ThenBy(x => x.OrdenKanban)
            .Select(x => new
            {
                x.Id,
                x.Titulo,
                ProjectName = x.Proyecto != null ? x.Proyecto.Nombre : string.Empty,
                DeveloperName = x.Developer != null ? x.Developer.FullName : x.Responsable != null ? x.Responsable.FullName : null,
                ResponsibleName = x.Responsable != null ? x.Responsable.FullName : null,
                Priority = x.Prioridad,
                DueDate = x.FechaVencimiento,
                Status = x.Estado,
                x.OrdenKanban
            })
            .ToListAsync(cancellationToken);

        var statusOrder = new[] { "Pendiente", "EnCurso", "Bloqueada", "Finalizada", "Cancelada" };

        return statusOrder
            .Select(status => new TaskKanbanColumnDto
            {
                Status = status,
                Title = ToUiStatus(status),
                Items = items
                    .Where(x => x.Status.ToString() == status)
                    .Select(x => new TaskKanbanItemDto
                    {
                        Id = x.Id,
                        Title = x.Titulo,
                        ProjectName = x.ProjectName,
                        DeveloperName = x.DeveloperName,
                        ResponsibleName = x.ResponsibleName,
                        Priority = x.Priority.ToString(),
                        DueDate = x.DueDate,
                        KanbanOrder = x.OrdenKanban
                    })
                    .ToList()
            })
            .ToList();
    }

    public async Task<TaskLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.Nombre)
            .Take(300)
            .Select(x => new TaskLookupItemDto { Id = x.Id, Name = x.Nombre })
            .ToListAsync(cancellationToken);

        var developers = await _dbContext.Developers
            .AsNoTracking()
            .Where(x => x.Activo && (!x.Users.Any() || x.Users.Any(u => u.UserRoles.Any(r => r.Role.Activo
                && (r.Role.Name == RoleNames.Analyst || r.Role.Name == RoleNames.Developer)))))
            .OrderBy(x => x.FullName)
            .Take(300)
            .Select(x => new TaskLookupItemDto { Id = x.Id, Name = x.FullName })
            .ToListAsync(cancellationToken);

        return new TaskLookupsDto
        {
            Projects = projects,
            Developers = developers
        };
    }

    private IQueryable<TaskItem> BuildFilteredQuery(TaskListRequestDto request)
    {
        var query = _dbContext.Tasks
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Titulo, search));
        }

        if (request.ProjectId.HasValue)
        {
            query = query.Where(x => x.ProyectoId == request.ProjectId);
        }

        if (request.DeveloperId.HasValue)
        {
            query = query.Where(x => x.DeveloperId == request.DeveloperId);
        }
        else if (request.ResponsibleId.HasValue)
        {
            query = query.Where(x => x.ResponsableId == request.ResponsibleId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<DomainTaskStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Estado == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Priority) && Enum.TryParse<TaskPriority>(request.Priority, true, out var priority))
        {
            query = query.Where(x => x.Prioridad == priority);
        }

        if (request.DueDateFrom.HasValue)
        {
            query = query.Where(x => x.FechaVencimiento >= request.DueDateFrom.Value);
        }

        if (request.DueDateTo.HasValue)
        {
            query = query.Where(x => x.FechaVencimiento <= request.DueDateTo.Value);
        }

        return query;
    }

    private static void ApplyRequest(TaskItem task, TaskUpsertRequestDto request)
    {
        task.ProyectoId = request.ProjectId;
        task.Titulo = request.Title.Trim();
        task.Descripcion = Normalize(request.Description);
        task.DeveloperId = request.DeveloperId;
        task.ResponsableId = request.DeveloperId.HasValue ? null : request.ResponsibleId;
        task.Estado = ParseStatus(request.Status);
        task.Prioridad = ParsePriority(request.Priority);
        task.FechaInicio = request.StartDate;
        task.FechaVencimiento = request.DueDate;
        task.FechaFinalizacion = request.FinishedDate;
        task.HorasEstimadas = request.EstimatedHours;
        task.HorasReales = request.RealHours;
        task.OrdenKanban = request.KanbanOrder;
        task.Activo = request.IsActive;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static DomainTaskStatus ParseStatus(string status)
    {
        return Enum.TryParse<DomainTaskStatus>(status, true, out var parsed)
            ? parsed
            : DomainTaskStatus.Pendiente;
    }

    private static TaskPriority ParsePriority(string priority)
    {
        return Enum.TryParse<TaskPriority>(priority, true, out var parsed)
            ? parsed
            : TaskPriority.Media;
    }

    private static string ToUiStatus(string status)
    {
        return status switch
        {
            "EnCurso" => "En curso",
            _ => status
        };
    }

    private static Expression<Func<TaskItem, TaskDetailProjection>> ToDetailProjection()
    {
        return x => new TaskDetailProjection
        {
            Id = x.Id,
            ProjectId = x.ProyectoId,
            ProjectName = x.Proyecto != null ? x.Proyecto.Nombre : string.Empty,
            Title = x.Titulo,
            DeveloperId = x.DeveloperId,
            DeveloperName = x.Developer != null ? x.Developer.FullName : x.Responsable != null ? x.Responsable.FullName : null,
            Description = x.Descripcion,
            ResponsibleId = x.ResponsableId,
            ResponsibleName = x.Responsable != null ? x.Responsable.FullName : null,
            CreatedById = x.CreadoPorId,
            CreatedByName = x.CreadoPor != null ? x.CreadoPor.FullName : string.Empty,
            Status = x.Estado,
            Priority = x.Prioridad,
            StartDate = x.FechaInicio,
            DueDate = x.FechaVencimiento,
            FinishedDate = x.FechaFinalizacion,
            EstimatedHours = x.HorasEstimadas,
            RealHours = x.HorasReales,
            KanbanOrder = x.OrdenKanban,
            IsActive = x.Activo,
            CreatedAt = x.FechaCreacion,
            UpdatedAt = x.FechaActualizacion
        };
    }

    private static TaskDetailDto MapToDetailDto(TaskDetailProjection source)
    {
        return new TaskDetailDto
        {
            Id = source.Id,
            ProjectId = source.ProjectId,
            ProjectName = source.ProjectName,
            Title = source.Title,
            Description = source.Description,
            DeveloperId = source.DeveloperId,
            DeveloperName = source.DeveloperName,
            ResponsibleId = source.ResponsibleId,
            ResponsibleName = source.ResponsibleName,
            CreatedById = source.CreatedById,
            CreatedByName = source.CreatedByName,
            Status = source.Status.ToString(),
            Priority = source.Priority.ToString(),
            StartDate = source.StartDate,
            DueDate = source.DueDate,
            FinishedDate = source.FinishedDate,
            EstimatedHours = source.EstimatedHours,
            RealHours = source.RealHours,
            KanbanOrder = source.KanbanOrder,
            IsActive = source.IsActive,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private sealed class TaskDetailProjection
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public Guid? DeveloperId { get; init; }
        public string? DeveloperName { get; init; }
        public Guid? ResponsibleId { get; init; }
        public string? ResponsibleName { get; init; }
        public Guid CreatedById { get; init; }
        public string CreatedByName { get; init; } = string.Empty;
        public DomainTaskStatus Status { get; init; }
        public TaskPriority Priority { get; init; }
        public DateOnly? StartDate { get; init; }
        public DateOnly? DueDate { get; init; }
        public DateOnly? FinishedDate { get; init; }
        public decimal? EstimatedHours { get; init; }
        public decimal? RealHours { get; init; }
        public int KanbanOrder { get; init; }
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }

    private async Task ValidateReferencesAsync(Guid? createdById, TaskUpsertRequestDto request, CancellationToken cancellationToken, Guid? currentDeveloperId = null)
    {
        var projectExists = await _dbContext.Projects.AnyAsync(x => x.Id == request.ProjectId, cancellationToken);
        if (!projectExists)
        {
            throw new ArgumentException("El proyecto seleccionado no existe");
        }

        if (request.DeveloperId.HasValue)
        {
            var candidates = _dbContext.Developers.Where(x => x.Id == request.DeveloperId.Value);
            if (request.DeveloperId != currentDeveloperId)
                candidates = candidates.Where(x => x.Activo && (!x.Users.Any() || x.Users.Any(u => u.UserRoles.Any(r => r.Role.Activo
                    && (r.Role.Name == RoleNames.Analyst || r.Role.Name == RoleNames.Developer)))));
            var developerExists = await candidates.AnyAsync(cancellationToken);
            if (!developerExists)
            {
                throw new ArgumentException("El desarrollador seleccionado no existe");
            }
        }
        else if (request.ResponsibleId.HasValue)
        {
            var responsibleExists = await _dbContext.Users.AnyAsync(x => x.Id == request.ResponsibleId.Value, cancellationToken);
            if (!responsibleExists)
            {
                throw new ArgumentException("El responsable seleccionado no existe");
            }
        }

        if (createdById.HasValue)
        {
            var creatorExists = await _dbContext.Users.AnyAsync(x => x.Id == createdById.Value, cancellationToken);
            if (!creatorExists)
            {
                throw new ArgumentException("El usuario creador no existe");
            }
        }
    }
}
