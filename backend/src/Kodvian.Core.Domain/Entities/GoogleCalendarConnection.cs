namespace Kodvian.Core.Domain.Entities;

/// <summary>Cuenta de Google que cada usuario conecta para que sus reuniones de Kodvian se agenden en su Google Calendar.</summary>
public sealed class GoogleCalendarConnection
{
    public Guid UserId { get; set; }
    public string GoogleEmail { get; set; } = string.Empty;
    /// <summary>Refresh token cifrado con Data Protection; nunca se devuelve al frontend.</summary>
    public string ProtectedRefreshToken { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAtUtc { get; set; }
    /// <summary>Último error de la conexión (por ejemplo, permiso revocado desde Google).</summary>
    public string? LastError { get; set; }
}
