using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Meetings.Requests;

public class MeetingListRequestDto : PagedRequestDto
{
    public Guid? ProjectId { get; set; }
    public Guid? ParticipantId { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
