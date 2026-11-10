using Kodvian.Core.Domain.Enums;

namespace Kodvian.Core.Domain.Entities;

public class Meeting : BaseEntity
{
    public Guid ProyectoId { get; set; }
    public Guid CreadoPorId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public MeetingPriority Prioridad { get; set; } = MeetingPriority.Media;
    public MeetingStatus Estado { get; set; } = MeetingStatus.Programada;
    public string? Enlace { get; set; }
    public string? Ubicacion { get; set; }
    /// <summary>Evento de Google Calendar creado para esta reunión (en el calendario de GoogleCalendarOwnerUserId).</summary>
    public string? GoogleEventId { get; set; }
    public Guid? GoogleCalendarOwnerUserId { get; set; }
    /// <summary>Synced, Failed o null si no se sincroniza (el organizador no conectó Google Calendar).</summary>
    public string? GoogleSyncStatus { get; set; }
    public string? GoogleSyncError { get; set; }
    public DateTime? GoogleSyncedAtUtc { get; set; }

    public Project? Proyecto { get; set; }
    public User? CreadoPor { get; set; }
    public ICollection<MeetingParticipant> Participantes { get; set; } = new List<MeetingParticipant>();
}
