namespace Kodvian.Core.Application.Integrations.GoogleCalendar;

public sealed class GoogleCalendarStatusDto
{
    /// <summary>False si el servidor no tiene configurado el cliente OAuth de Google (la función queda deshabilitada).</summary>
    public bool Enabled { get; init; }
    public bool Connected { get; init; }
    public string? Email { get; init; }
    public DateTime? ConnectedAtUtc { get; init; }
    public DateTime? LastSyncAtUtc { get; init; }
    public string? LastError { get; init; }
}

public sealed class GoogleCalendarCallbackResult
{
    public bool Success { get; init; }
    /// <summary>Motivo corto para mostrar en la pantalla (denied, invalid_state, exchange_failed, disabled).</summary>
    public string? Reason { get; init; }
}

public sealed class GoogleCalendarSyncResultDto
{
    public string? Status { get; init; }
    public string? Error { get; init; }
}

public interface IGoogleCalendarService
{
    Task<GoogleCalendarStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Arma la URL de autorización de Google con state firmado y PKCE.</summary>
    string BuildAuthorizationUrl(Guid userId);
    Task<GoogleCalendarCallbackResult> HandleCallbackAsync(string? code, string? state, string? error, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Reintento manual de una reunión que no se pudo sincronizar (solo el organizador o un Administrador).</summary>
    Task<GoogleCalendarSyncResultDto?> RetryMeetingAsync(Guid userId, bool isAdministrator, Guid meetingId, CancellationToken cancellationToken);
}

/// <summary>Sincroniza una reunión de Kodvian con el Google Calendar del organizador. Nunca lanza excepciones.</summary>
public interface IMeetingCalendarSync
{
    Task SyncMeetingAsync(Guid meetingId, CancellationToken cancellationToken);
}
