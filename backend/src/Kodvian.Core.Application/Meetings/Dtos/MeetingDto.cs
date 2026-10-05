namespace Kodvian.Core.Application.Meetings.Dtos;

public class MeetingDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectCalendarColor { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Link { get; set; }
    public string? Location { get; set; }
    public Guid CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public IReadOnlyCollection<MeetingParticipantDto> Participants { get; set; } = [];
}

public class MeetingParticipantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
