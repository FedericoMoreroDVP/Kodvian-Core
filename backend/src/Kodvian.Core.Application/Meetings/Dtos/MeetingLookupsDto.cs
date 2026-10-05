namespace Kodvian.Core.Application.Meetings.Dtos;

public class MeetingLookupsDto
{
    public IReadOnlyCollection<MeetingLookupItemDto> Projects { get; set; } = [];
    public IReadOnlyCollection<MeetingLookupItemDto> Users { get; set; } = [];
}

public class MeetingLookupItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CalendarColor { get; set; }
}
