namespace Kodvian.Core.Domain.Entities;

/// <summary>Preferencias de cada usuario para Oficina Inteligente; se inyectan en el prompt de sistema.</summary>
public sealed class AiUserPreference
{
    public Guid UserId { get; set; }
    /// <summary>ARS o USD: moneda que se muestra primero (nunca se suman monedas).</summary>
    public string? PreferredCurrency { get; set; }
    public int? DefaultMeetingMinutes { get; set; }
    public string? DefaultTaskPriority { get; set; }
    /// <summary>breve o detallado.</summary>
    public string? ResponseStyle { get; set; }
    /// <summary>todo o mio: alcance por defecto de "mi día" y de las consultas personales.</summary>
    public string? DefaultScope { get; set; }
    /// <summary>Indicaciones libres del usuario ("cuando digo el portal me refiero al proyecto X").</summary>
    public string? Notes { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
