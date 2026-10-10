using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Tasks.Requests;

public class TaskListRequestDto : PagedRequestDto
{
    public string? Search { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? DeveloperId { get; set; }
    public Guid? ResponsibleId { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public DateOnly? DueDateFrom { get; set; }
    public DateOnly? DueDateTo { get; set; }
    public bool CompletedHistory { get; set; }
}
