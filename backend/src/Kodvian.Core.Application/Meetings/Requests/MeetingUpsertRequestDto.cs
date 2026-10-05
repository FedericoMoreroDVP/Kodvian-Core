namespace Kodvian.Core.Application.Meetings.Requests;

public class MeetingUpsertRequestDto
{
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Priority { get; set; } = "Media";
    public string Status { get; set; } = "Programada";
    public string? Link { get; set; }
    public string? Location { get; set; }
    public IReadOnlyCollection<Guid> ParticipantIds { get; set; } = [];
}
