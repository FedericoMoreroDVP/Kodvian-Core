using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kodvian.Core.Application.Integrations.GoogleCalendar;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Infrastructure.GoogleCalendar;

/// <summary>
/// Conexión de cada usuario con su Google Calendar (OAuth 2.0 con PKCE y state firmado) y sincronización
/// de las reuniones de Kodvian en el calendario del organizador, con los participantes como invitados.
/// La sincronización nunca bloquea: si Google falla, la reunión queda guardada y marcada como no sincronizada.
/// </summary>
public sealed class GoogleCalendarService(
    KodvianDbContext dbContext,
    IGoogleCalendarClient client,
    IOptions<GoogleCalendarOptions> options,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<GoogleCalendarService> logger) : IGoogleCalendarService, IMeetingCalendarSync
{
    public const string Scopes = "openid email https://www.googleapis.com/auth/calendar.events";
    public const string TimeZone = "America/Argentina/Buenos_Aires";
    internal static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly GoogleCalendarOptions _options = options.Value;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("Kodvian.Core.GoogleCalendar.OAuthState.v1");
    private readonly IDataProtector _tokenProtector = dataProtectionProvider.CreateProtector("Kodvian.Core.GoogleCalendar.RefreshToken.v1");

    private sealed record OAuthState(Guid UserId, string CodeVerifier, string Nonce, DateTime IssuedAtUtc);

    public async Task<GoogleCalendarStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = await dbContext.GoogleCalendarConnections.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return new GoogleCalendarStatusDto
        {
            Enabled = _options.Enabled,
            Connected = connection is not null,
            Email = connection?.GoogleEmail,
            ConnectedAtUtc = connection?.ConnectedAtUtc,
            LastSyncAtUtc = connection?.LastSyncAtUtc,
            LastError = connection?.LastError
        };
    }

    public string BuildAuthorizationUrl(Guid userId)
    {
        if (!_options.Enabled) throw new InvalidOperationException("La integración con Google Calendar no está configurada.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = _stateProtector.Protect(JsonSerializer.Serialize(new OAuthState(userId, verifier, Base64Url(RandomNumberGenerator.GetBytes(16)), DateTime.UtcNow)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId!,
            ["redirect_uri"] = _options.RedirectUri!,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            // offline + consent: Google entrega un refresh token para sincronizar sin que el usuario esté en la pantalla.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        return GoogleCalendarHttpClient.AuthorizationEndpoint + "?" + string.Join("&", query.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task<GoogleCalendarCallbackResult> HandleCallbackAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return new GoogleCalendarCallbackResult { Reason = "disabled" };
        if (!string.IsNullOrWhiteSpace(error)) return new GoogleCalendarCallbackResult { Reason = "denied" };
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return new GoogleCalendarCallbackResult { Reason = "invalid_state" };

        OAuthState? payload;
        try { payload = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(state)); }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return new GoogleCalendarCallbackResult { Reason = "invalid_state" };
        }
        // El state vence a los 10 minutos y está cifrado y firmado: no se puede reutilizar ni fabricar para otro usuario.
        if (payload is null || DateTime.UtcNow - payload.IssuedAtUtc > StateLifetime || payload.IssuedAtUtc > DateTime.UtcNow.AddMinutes(1)
            || !await dbContext.Users.AnyAsync(x => x.Id == payload.UserId && x.Activo, cancellationToken))
            return new GoogleCalendarCallbackResult { Reason = "invalid_state" };

        GoogleTokenResult tokens;
        try
        {
            tokens = await client.ExchangeCodeAsync(code, payload.CodeVerifier, cancellationToken);
        }
        catch (GoogleCalendarException exception)
        {
            logger.LogWarning("Google Calendar code exchange failed for user {UserId}: {Message}", payload.UserId, exception.Message);
            return new GoogleCalendarCallbackResult { Reason = "exchange_failed" };
        }
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken)) return new GoogleCalendarCallbackResult { Reason = "exchange_failed" };

        var connection = await dbContext.GoogleCalendarConnections.SingleOrDefaultAsync(x => x.UserId == payload.UserId, cancellationToken);
        if (connection is null)
        {
            connection = new GoogleCalendarConnection { UserId = payload.UserId };
            dbContext.GoogleCalendarConnections.Add(connection);
        }
        connection.GoogleEmail = tokens.Email ?? "Cuenta de Google";
        connection.ProtectedRefreshToken = _tokenProtector.Protect(tokens.RefreshToken);
        connection.ConnectedAtUtc = DateTime.UtcNow;
        connection.LastError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new GoogleCalendarCallbackResult { Success = true };
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = await dbContext.GoogleCalendarConnections.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (connection is null) return;
        try { await client.RevokeAsync(_tokenProtector.Unprotect(connection.ProtectedRefreshToken), cancellationToken); }
        catch (Exception exception) when (exception is CryptographicException or GoogleCalendarException or HttpRequestException)
        {
            logger.LogInformation("Google Calendar revoke for user {UserId} failed; the connection is removed anyway.", userId);
        }
        dbContext.GoogleCalendarConnections.Remove(connection);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoogleCalendarSyncResultDto?> RetryMeetingAsync(Guid userId, bool isAdministrator, Guid meetingId, CancellationToken cancellationToken)
    {
        var meeting = await dbContext.Meetings.AsNoTracking().Where(x => x.Id == meetingId)
            .Select(x => new { x.CreadoPorId, x.GoogleCalendarOwnerUserId }).SingleOrDefaultAsync(cancellationToken);
        if (meeting is null) return null;
        if (!isAdministrator && meeting.CreadoPorId != userId && meeting.GoogleCalendarOwnerUserId != userId)
            throw new UnauthorizedAccessException("Solo el organizador puede reintentar la sincronización.");
        await SyncMeetingAsync(meetingId, cancellationToken);
        var result = await dbContext.Meetings.AsNoTracking().Where(x => x.Id == meetingId)
            .Select(x => new GoogleCalendarSyncResultDto { Status = x.GoogleSyncStatus, Error = x.GoogleSyncError }).SingleAsync(cancellationToken);
        return result;
    }

    public async Task SyncMeetingAsync(Guid meetingId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        try
        {
            // Solo la reunión queda bajo seguimiento: proyecto y participantes se leen aparte para no interferir con Agenda,
            // que comparte el mismo DbContext.
            var meeting = await dbContext.Meetings
                .SingleOrDefaultAsync(x => x.Id == meetingId, cancellationToken);
            if (meeting is null) return;

            // El evento vive en el calendario de quien la organizó (o de quien la sincronizó la primera vez).
            var ownerId = meeting.GoogleCalendarOwnerUserId ?? meeting.CreadoPorId;
            var connection = await dbContext.GoogleCalendarConnections.SingleOrDefaultAsync(x => x.UserId == ownerId, cancellationToken);
            if (connection is null)
            {
                if (meeting.GoogleEventId is not null)
                {
                    meeting.GoogleSyncStatus = "Failed";
                    meeting.GoogleSyncError = "El organizador desconectó Google Calendar.";
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            try
            {
                var accessToken = await client.RefreshAccessTokenAsync(_tokenProtector.Unprotect(connection.ProtectedRefreshToken), cancellationToken);
                var cancelled = meeting.Estado == MeetingStatus.Cancelada || !meeting.Activo;
                if (cancelled)
                {
                    if (meeting.GoogleEventId is not null) await client.DeleteEventAsync(accessToken, meeting.GoogleEventId, cancellationToken);
                }
                else
                {
                    var projectName = await dbContext.Projects.AsNoTracking().Where(x => x.Id == meeting.ProyectoId).Select(x => x.Nombre).SingleOrDefaultAsync(cancellationToken);
                    var participantEmails = await dbContext.MeetingParticipants.AsNoTracking().Where(x => x.MeetingId == meeting.Id && x.User != null)
                        .Select(x => x.User!.Email).ToListAsync(cancellationToken);
                    var calendarEvent = ToEvent(meeting, projectName, participantEmails, connection.GoogleEmail);
                    if (meeting.GoogleEventId is null)
                        meeting.GoogleEventId = await client.CreateEventAsync(accessToken, calendarEvent, cancellationToken);
                    else
                    {
                        try { await client.UpdateEventAsync(accessToken, meeting.GoogleEventId, calendarEvent, cancellationToken); }
                        catch (GoogleCalendarException exception) when (exception.NotFound)
                        {
                            // Lo borraron desde Google: se vuelve a crear.
                            meeting.GoogleEventId = await client.CreateEventAsync(accessToken, calendarEvent, cancellationToken);
                        }
                    }
                }
                meeting.GoogleCalendarOwnerUserId = ownerId;
                meeting.GoogleSyncStatus = "Synced";
                meeting.GoogleSyncError = null;
                meeting.GoogleSyncedAtUtc = DateTime.UtcNow;
                connection.LastSyncAtUtc = DateTime.UtcNow;
                connection.LastError = null;
            }
            catch (Exception exception) when (exception is GoogleCalendarException or CryptographicException)
            {
                var message = exception is GoogleCalendarException google ? google.Message : "No se pudo leer la conexión con Google; volvé a conectarla.";
                meeting.GoogleSyncStatus = "Failed";
                meeting.GoogleSyncError = message.Length <= 300 ? message : message[..300];
                if (exception is CryptographicException || exception is GoogleCalendarException { AuthorizationRevoked: true })
                    connection.LastError = "Google rechazó el acceso: volvé a conectar Google Calendar.";
                logger.LogWarning("Google Calendar sync failed for meeting {MeetingId}: {Message}", meetingId, message);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nunca se bloquea la operación de Agenda por un problema de sincronización.
            logger.LogError(exception, "Unexpected error while syncing meeting {MeetingId} with Google Calendar.", meetingId);
        }
    }

    internal static GoogleCalendarEvent ToEvent(Meeting meeting, string? projectName, IEnumerable<string> participantEmails, string ownerEmail)
    {
        var description = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(meeting.Descripcion)) description.AppendLine(meeting.Descripcion).AppendLine();
        if (!string.IsNullOrWhiteSpace(meeting.Enlace)) description.AppendLine($"Enlace: {meeting.Enlace}");
        if (projectName is not null) description.AppendLine($"Proyecto: {projectName}");
        description.Append("Agendada desde Kodvian Core.");
        var attendees = participantEmails
            .Where(email => !string.IsNullOrWhiteSpace(email) && !string.Equals(email, ownerEmail, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new GoogleCalendarEvent(meeting.Titulo, description.ToString(), meeting.Ubicacion,
            DateTime.SpecifyKind(meeting.Inicio, DateTimeKind.Utc), DateTime.SpecifyKind(meeting.Fin, DateTimeKind.Utc), attendees, TimeZone);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
