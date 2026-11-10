using System.Text.RegularExpressions;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>
/// El modelo cierra cada respuesta con una línea "[[sugerencias: a | b | c]]".
/// Se separa del texto visible y se guarda aparte para mostrarla como botones.
/// </summary>
public static partial class AiSuggestionParser
{
    public const int MaxSuggestions = 3;
    public const int MaxSuggestionLength = 90;

    [GeneratedRegex(@"\[\[\s*sugerencias\s*:(?<items>[^\]]*)\]\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerRegex();

    public static (string Text, IReadOnlyList<string> Suggestions) Extract(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, []);
        var suggestions = new List<string>();
        foreach (Match match in MarkerRegex().Matches(text))
        {
            foreach (var raw in match.Groups["items"].Value.Split('|'))
            {
                var item = raw.Trim().Trim('"', '“', '”', '-', '•').Trim();
                if (item.Length is 0 or > MaxSuggestionLength) continue;
                if (suggestions.Contains(item, StringComparer.OrdinalIgnoreCase)) continue;
                if (suggestions.Count < MaxSuggestions) suggestions.Add(item);
            }
        }
        var clean = MarkerRegex().Replace(text, string.Empty).TrimEnd();
        return (clean, suggestions);
    }
}
