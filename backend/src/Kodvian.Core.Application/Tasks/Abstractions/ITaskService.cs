using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Tasks.Dtos;
using Kodvian.Core.Application.Tasks.Requests;

namespace Kodvian.Core.Application.Tasks.Abstractions;

public interface ITaskService
{
    Task<PagedResultDto<TaskListItemDto>> GetPagedAsync(TaskListRequestDto request, CancellationToken cancellationToken = default);
    Task<TaskDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaskDetailDto> CreateAsync(Guid createdById, TaskUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<TaskDetailDto?> UpdateAsync(Guid id, TaskUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<TaskDetailDto?> UpdateStatusAsync(Guid id, TaskStatusUpdateRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TaskKanbanColumnDto>> GetKanbanAsync(TaskListRequestDto request, CancellationToken cancellationToken = default);
    Task<TaskLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default);
}
