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

    public Project? Proyecto { get; set; }
    public User? CreadoPor { get; set; }
    public ICollection<MeetingParticipant> Participantes { get; set; } = new List<MeetingParticipant>();
}
