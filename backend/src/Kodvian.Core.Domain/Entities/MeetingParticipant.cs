namespace Kodvian.Core.Domain.Entities;

public class MeetingParticipant : BaseEntity
{
    public Guid MeetingId { get; set; }
    public Guid UserId { get; set; }

    public Meeting? Meeting { get; set; }
    public User? User { get; set; }
}
