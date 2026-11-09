using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Clients.Requests;

public class ClientListRequestDto : PagedRequestDto
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public bool ActiveOnly { get; set; }
}
