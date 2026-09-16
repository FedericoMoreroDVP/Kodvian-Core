namespace Kodvian.Core.Application.Projects;

public static class GoogleDriveLinkRules
{
    public static string? Normalize(string? value)
    {
        var link = value?.Trim();
        if (string.IsNullOrEmpty(link)) return null;

        if (link.Length > 2048 || link.Any(char.IsWhiteSpace) || link.Any(char.IsControl) || link.Contains('\\')
            || !Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath == "/")
        {
            throw new ArgumentException("Ingresa un enlace HTTPS válido de Google Drive (https://drive.google.com/...), de hasta 2048 caracteres.");
        }

        // Shared links may require query parameters such as resourcekey.
        return link;
    }
}
