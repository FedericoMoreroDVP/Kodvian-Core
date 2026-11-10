namespace Kodvian.Core.Application.Ai.Dtos;

public sealed class AiUserPreferencesDto
{
    public string? PreferredCurrency { get; set; }
    public int? DefaultMeetingMinutes { get; set; }
    public string? DefaultTaskPriority { get; set; }
    public string? ResponseStyle { get; set; }
    public string? DefaultScope { get; set; }
    public string? Notes { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public bool IsEmpty => PreferredCurrency is null && DefaultMeetingMinutes is null && DefaultTaskPriority is null
        && ResponseStyle is null && DefaultScope is null && string.IsNullOrWhiteSpace(Notes);
}

public static class AiPreferenceRules
{
    public static readonly string[] Currencies = ["ARS", "USD"];
    public static readonly string[] Priorities = ["Baja", "Media", "Alta", "Urgente"];
    public static readonly string[] Styles = ["breve", "detallado"];
    public static readonly string[] Scopes = ["todo", "mio"];
    public const int MaxNotesLength = 500;

    /// <summary>Valida y normaliza; los valores vacíos significan "sin preferencia".</summary>
    public static AiUserPreferencesDto Normalize(AiUserPreferencesDto request)
    {
        static string? Option(string? value, string[] options, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return options.FirstOrDefault(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"El valor de {field} no es válido.");
        }

        if (request.DefaultMeetingMinutes is < 15 or > 480) throw new ArgumentException("La duración de las reuniones debe estar entre 15 y 480 minutos.");
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > MaxNotesLength) throw new ArgumentException($"Las indicaciones no pueden superar los {MaxNotesLength} caracteres.");
        return new AiUserPreferencesDto
        {
            PreferredCurrency = Option(request.PreferredCurrency, Currencies, "moneda"),
            DefaultMeetingMinutes = request.DefaultMeetingMinutes,
            DefaultTaskPriority = Option(request.DefaultTaskPriority, Priorities, "prioridad"),
            ResponseStyle = Option(request.ResponseStyle, Styles, "estilo"),
            DefaultScope = Option(request.DefaultScope, Scopes, "alcance"),
            Notes = notes
        };
    }
}
