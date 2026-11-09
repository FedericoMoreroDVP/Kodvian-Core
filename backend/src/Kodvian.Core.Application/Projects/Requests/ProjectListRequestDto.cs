using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Projects.Requests;

public class ProjectListRequestDto : PagedRequestDto
{
    public string? Search { get; set; }
    public Guid? ClientId { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public bool ActiveOnly { get; set; }
}
