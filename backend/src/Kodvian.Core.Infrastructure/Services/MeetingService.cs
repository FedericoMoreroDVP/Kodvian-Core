using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Meetings.Abstractions;
using Kodvian.Core.Application.Meetings.Dtos;
using Kodvian.Core.Application.Meetings.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class MeetingService : IMeetingService
{
    private readonly KodvianDbContext _dbContext;

    public MeetingService(KodvianDbContext dbContext) => _dbContext = dbContext;

    public async Task<PagedResultDto<MeetingDto>> GetPagedAsync(Guid userId, bool isAdministrator, MeetingListRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = await BuildAccessibleQueryAsync(userId, isAdministrator, cancellationToken);
        if (request.ProjectId.HasValue) query = query.Where(x => x.ProyectoId == request.ProjectId);
        if (request.ParticipantId.HasValue) query = query.Where(x => x.Participantes.Any(p => p.UserId == request.ParticipantId));
        if (!string.IsNullOrWhiteSpace(request.Priority) && Enum.TryParse<MeetingPriority>(request.Priority, true, out var priority)) query = query.Where(x => x.Prioridad == priority);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<MeetingStatus>(request.Status, true, out var status)) query = query.Where(x => x.Estado == status);
        if (request.From.HasValue) query = query.Where(x => x.Fin >= ToUtc(request.From.Value));
        if (request.To.HasValue) query = query.Where(x => x.Inicio <= ToUtc(request.To.Value));

        var totalCount = await query.CountAsync(cancellationToken);
        var meetings = await query.OrderBy(x => x.Inicio).ThenBy(x => x.Titulo)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Include(x => x.Proyecto).Include(x => x.CreadoPor).Include(x => x.Participantes).ThenInclude(x => x.User)
            .ToListAsync(cancellationToken);

        return new PagedResultDto<MeetingDto>
        {
            Items = meetings.Select(ToDto).ToList(), PageNumber = request.PageNumber, PageSize = request.PageSize, TotalCount = totalCount
        };
    }

    public async Task<MeetingDto?> GetByIdAsync(Guid userId, bool isAdministrator, Guid id, CancellationToken cancellationToken = default)
    {
        var query = await BuildAccessibleQueryAsync(userId, isAdministrator, cancellationToken);
        var meeting = await query.Where(x => x.Id == id).Include(x => x.Proyecto).Include(x => x.CreadoPor)
            .Include(x => x.Participantes).ThenInclude(x => x.User).FirstOrDefaultAsync(cancellationToken);
        return meeting is null ? null : ToDto(meeting);
    }

    public async Task<MeetingDto> CreateAsync(Guid userId, bool isAdministrator, MeetingUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(userId, isAdministrator, request, cancellationToken);
        var meeting = new Meeting { CreadoPorId = userId };
        ApplyRequest(meeting, request);
        AddParticipants(meeting, request.ParticipantIds);
        _dbContext.Meetings.Add(meeting);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetByIdAsync(userId, true, meeting.Id, cancellationToken))!;
    }

    public async Task<MeetingDto?> UpdateAsync(Guid userId, bool isAdministrator, Guid id, MeetingUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(userId, isAdministrator, id, cancellationToken)) return null;
        var meeting = await _dbContext.Meetings.Include(x => x.Participantes).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (meeting is null) return null;
        if (meeting.Estado == MeetingStatus.Cancelada) throw new ArgumentException("No se puede editar una reunión cancelada");
        await ValidateReferencesAsync(userId, isAdministrator, request, cancellationToken);
        ApplyRequest(meeting, request);
        _dbContext.MeetingParticipants.RemoveRange(meeting.Participantes);
        meeting.Participantes.Clear();
        AddParticipants(meeting, request.ParticipantIds);
        meeting.FechaActualizacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(userId, isAdministrator, id, cancellationToken);
    }

    public async Task<MeetingDto?> CancelAsync(Guid userId, bool isAdministrator, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(userId, isAdministrator, id, cancellationToken)) return null;

        var meeting = await _dbContext.Meetings.Include(x => x.Proyecto).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (meeting is null) return null;
        if (meeting.Proyecto?.Estado == ProjectStatus.Cancelado) throw new ArgumentException("No se pueden modificar reuniones de un proyecto cancelado");
        meeting.Estado = MeetingStatus.Cancelada;
        meeting.FechaActualizacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(userId, isAdministrator, id, cancellationToken);
    }

    public async Task<MeetingLookupsDto> GetLookupsAsync(Guid userId, bool isAdministrator, bool canWrite, CancellationToken cancellationToken = default)
    {
        var accessibleProjects = await BuildAccessibleProjectsQueryAsync(userId, isAdministrator, cancellationToken);
        var projects = await accessibleProjects
            .OrderBy(x => x.Nombre).Select(x => new MeetingLookupItemDto { Id = x.Id, Name = x.Nombre, CalendarColor = x.CalendarColor }).ToListAsync(cancellationToken);
        var users = canWrite
            ? await _dbContext.Users.AsNoTracking().Where(x => x.Activo).OrderBy(x => x.FullName).Take(300)
                .Select(x => new MeetingLookupItemDto { Id = x.Id, Name = x.FullName }).ToListAsync(cancellationToken)
            : [];
        return new MeetingLookupsDto { Projects = projects, Users = users };
    }

    private async Task<IQueryable<Meeting>> BuildAccessibleQueryAsync(Guid userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var query = _dbContext.Meetings.AsNoTracking().AsQueryable();
        if (isAdministrator) return query;
        var developerId = await GetDeveloperIdAsync(userId, cancellationToken);
        return query.Where(x => x.CreadoPorId == userId || x.Proyecto!.ResponsableId == userId || x.Participantes.Any(p => p.UserId == userId)
            || (developerId.HasValue && x.Proyecto!.DeveloperAssignments.Any(a => a.Activo && a.DeveloperId == developerId)));
    }

    private async Task<IQueryable<Project>> BuildAccessibleProjectsQueryAsync(Guid userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var query = _dbContext.Projects.AsNoTracking().Where(x => x.Activo);
        if (isAdministrator) return query;
        var developerId = await GetDeveloperIdAsync(userId, cancellationToken);
        return query.Where(x => x.ResponsableId == userId || (developerId.HasValue && x.DeveloperAssignments.Any(a => a.Activo && a.DeveloperId == developerId)));
    }

    private Task<Guid?> GetDeveloperIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Users.Where(x => x.Id == userId).Select(x => x.DeveloperId).FirstOrDefaultAsync(cancellationToken);

    private async Task ValidateReferencesAsync(Guid userId, bool isAdministrator, MeetingUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var projectExists = await _dbContext.Projects.AnyAsync(x => x.Id == request.ProjectId && x.Activo && x.Estado != ProjectStatus.Cancelado, cancellationToken);
        if (!projectExists) throw new ArgumentException("El proyecto seleccionado no existe");
        if (!isAdministrator)
        {
            var developerId = await GetDeveloperIdAsync(userId, cancellationToken);
            var canAccessProject = await _dbContext.Projects.AnyAsync(x => x.Id == request.ProjectId
                && (x.ResponsableId == userId || (developerId.HasValue && x.DeveloperAssignments.Any(a => a.Activo && a.DeveloperId == developerId))), cancellationToken);
            if (!canAccessProject) throw new UnauthorizedAccessException("No tienes acceso al proyecto seleccionado");
        }
        var participantIds = request.ParticipantIds.Distinct().ToArray();
        var participantsCount = await _dbContext.Users.CountAsync(x => x.Activo && participantIds.Contains(x.Id), cancellationToken);
        if (participantsCount != participantIds.Length) throw new ArgumentException("Uno o más participantes no existen o están inactivos");
    }

    private static void ApplyRequest(Meeting meeting, MeetingUpsertRequestDto request)
    {
        meeting.ProyectoId = request.ProjectId;
        meeting.Titulo = request.Title.Trim();
        meeting.Descripcion = Normalize(request.Description);
        meeting.Inicio = ToUtc(request.StartsAt);
        meeting.Fin = ToUtc(request.EndsAt);
        meeting.Prioridad = Enum.Parse<MeetingPriority>(request.Priority, true);
        meeting.Estado = Enum.Parse<MeetingStatus>(request.Status, true);
        meeting.Enlace = Normalize(request.Link);
        meeting.Ubicacion = Normalize(request.Location);
        meeting.Activo = true;
    }

    private static void AddParticipants(Meeting meeting, IReadOnlyCollection<Guid> participantIds)
    {
        foreach (var userId in participantIds.Distinct()) meeting.Participantes.Add(new MeetingParticipant { UserId = userId });
    }

    private static MeetingDto ToDto(Meeting meeting) => new()
    {
        Id = meeting.Id, ProjectId = meeting.ProyectoId, ProjectName = meeting.Proyecto?.Nombre ?? string.Empty, ProjectCalendarColor = meeting.Proyecto?.CalendarColor ?? "#5AB0FF",
        Title = meeting.Titulo, Description = meeting.Descripcion, StartsAt = meeting.Inicio, EndsAt = meeting.Fin,
        Priority = meeting.Prioridad.ToString(), Status = meeting.Estado.ToString(), Link = meeting.Enlace, Location = meeting.Ubicacion,
        CreatedById = meeting.CreadoPorId, CreatedByName = meeting.CreadoPor?.FullName ?? string.Empty,
        Participants = meeting.Participantes.OrderBy(x => x.User!.FullName).Select(x => new MeetingParticipantDto { Id = x.UserId, Name = x.User!.FullName }).ToList()
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private Task<bool> CanManageAsync(Guid userId, bool isAdministrator, Guid meetingId, CancellationToken cancellationToken) =>
        isAdministrator
            ? Task.FromResult(true)
            : _dbContext.Meetings.AnyAsync(x => x.Id == meetingId && (x.CreadoPorId == userId || x.Proyecto!.ResponsableId == userId), cancellationToken);
}
