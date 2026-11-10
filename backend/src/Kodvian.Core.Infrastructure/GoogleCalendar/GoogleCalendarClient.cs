using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kodvian.Core.Infrastructure.GoogleCalendar;

/// <summary>
/// Cliente OAuth de Google para la integración con Google Calendar. Se configura con variables de entorno
/// (GOOGLE_CALENDAR_CLIENT_ID, GOOGLE_CALENDAR_CLIENT_SECRET, GOOGLE_CALENDAR_REDIRECT_URI) o con la sección GoogleCalendar.
/// Sin las tres, la integración queda deshabilitada.
/// </summary>
public sealed class GoogleCalendarOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(RedirectUri);
}

public sealed class GoogleCalendarException(string message, bool authorizationRevoked = false, bool notFound = false) : Exception(message)
{
    /// <summary>Google rechazó el refresh token (el usuario revocó el acceso o venció): hay que volver a conectar.</summary>
    public bool AuthorizationRevoked { get; } = authorizationRevoked;
    public bool NotFound { get; } = notFound;
}

public sealed record GoogleTokenResult(string AccessToken, string? RefreshToken, string? Email);

public sealed record GoogleCalendarEvent(string Summary, string? Description, string? Location, DateTime StartsAtUtc, DateTime EndsAtUtc, IReadOnlyCollection<string> AttendeeEmails, string TimeZone);

public interface IGoogleCalendarClient
{
    Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);
    Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken);
    Task<string> CreateEventAsync(string accessToken, GoogleCalendarEvent calendarEvent, CancellationToken cancellationToken);
    Task UpdateEventAsync(string accessToken, string eventId, GoogleCalendarEvent calendarEvent, CancellationToken cancellationToken);
    Task DeleteEventAsync(string accessToken, string eventId, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>Implementación con la API REST de Google (sin SDK): OAuth 2.0 y Calendar API v3 sobre el calendario principal.</summary>
public sealed class GoogleCalendarHttpClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<GoogleCalendarOptions> options) : IGoogleCalendarClient
{
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string EventsEndpoint = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    private readonly GoogleCalendarOptions _options = options.Value;

    public async Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var json = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = _options.ClientId!, ["client_secret"] = _options.ClientSecret!,
            ["redirect_uri"] = _options.RedirectUri!, ["grant_type"] = "authorization_code", ["code_verifier"] = codeVerifier
        }, cancellationToken);
        var accessToken = json["access_token"]?.GetValue<string>() ?? throw new GoogleCalendarException("Google no devolvió un token de acceso.");
        return new GoogleTokenResult(accessToken, json["refresh_token"]?.GetValue<string>(), EmailFromIdToken(json["id_token"]?.GetValue<string>()));
    }

    public async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var json = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken, ["client_id"] = _options.ClientId!, ["client_secret"] = _options.ClientSecret!, ["grant_type"] = "refresh_token"
        }, cancellationToken);
        return json["access_token"]?.GetValue<string>() ?? throw new GoogleCalendarException("Google no devolvió un token de acceso.");
    }

    public async Task<string> CreateEventAsync(string accessToken, GoogleCalendarEvent calendarEvent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{EventsEndpoint}?sendUpdates=all") { Content = JsonContent.Create(ToBody(calendarEvent)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var json = await SendAsync(request, cancellationToken);
        return json?["id"]?.GetValue<string>() ?? throw new GoogleCalendarException("Google no devolvió el identificador del evento.");
    }

    public async Task UpdateEventAsync(string accessToken, string eventId, GoogleCalendarEvent calendarEvent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{EventsEndpoint}/{Uri.EscapeDataString(eventId)}?sendUpdates=all") { Content = JsonContent.Create(ToBody(calendarEvent)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await SendAsync(request, cancellationToken);
    }

    public async Task DeleteEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{EventsEndpoint}/{Uri.EscapeDataString(eventId)}?sendUpdates=all");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try { await SendAsync(request, cancellationToken); }
        catch (GoogleCalendarException exception) when (exception.NotFound) { /* ya no existía en Google */ }
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken });
        using var response = await httpClient.PostAsync(RevokeEndpoint, content, cancellationToken);
        // Si ya estaba revocado Google responde 400: igual se considera desconectado.
    }

    private static object ToBody(GoogleCalendarEvent e) => new
    {
        summary = e.Summary,
        description = e.Description,
        location = e.Location,
        start = new { dateTime = DateTime.SpecifyKind(e.StartsAtUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), timeZone = e.TimeZone },
        end = new { dateTime = DateTime.SpecifyKind(e.EndsAtUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), timeZone = e.TimeZone },
        attendees = e.AttendeeEmails.Select(email => new { email }).ToArray()
    };

    private async Task<JsonObject> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        return await SendAsync(request, cancellationToken) ?? throw new GoogleCalendarException("Google devolvió una respuesta vacía.");
    }

    private async Task<JsonObject?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new GoogleCalendarException("No se pudo conectar con Google Calendar.");
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
            var googleError = TryReadError(text);
            if (googleError == "invalid_grant" || response.StatusCode == HttpStatusCode.Unauthorized)
                throw new GoogleCalendarException("Google rechazó el acceso: volvé a conectar Google Calendar.", authorizationRevoked: true);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                throw new GoogleCalendarException("El evento ya no existe en Google Calendar.", notFound: true);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new GoogleCalendarException("Google no permitió modificar el calendario (permisos o cuota).");
            throw new GoogleCalendarException($"Google Calendar respondió con error {(int)response.StatusCode}.");
        }
    }

    private static string? TryReadError(string text)
    {
        try { return JsonNode.Parse(text)?["error"] is JsonValue value && value.TryGetValue<string>(out var error) ? error : null; }
        catch (JsonException) { return null; }
    }

    // El id_token llega directo del endpoint de tokens de Google por TLS; solo se lee el email para mostrarlo.
    internal static string? EmailFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 }) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))?["email"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
