namespace Kodvian.Core.Application.Tasks;

public static class TaskAttachmentRules
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".webp"] = "image/webp",
        [".pdf"] = "application/pdf", [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain", [".csv"] = "text/csv", [".zip"] = "application/zip"
    };

    public static (string FileName, string Extension, string ContentType) Validate(string name, byte[] content)
    {
        var fileName = Path.GetFileName(name.Replace('\\', '/')).Trim();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (fileName.Length is 0 or > 255 || fileName.Any(char.IsControl) || !Types.TryGetValue(extension, out var type))
            throw new ArgumentException("Formato no permitido. Usa imágenes PNG/JPG/WebP, PDF, Word, Excel, TXT, CSV o ZIP.");
        if (content.Length == 0 || content.Length > MaxBytes)
            throw new ArgumentException("El archivo debe contener información y no superar los 10 MB.");

        bool Starts(params byte[] signature) => content.AsSpan().StartsWith(signature);
        var valid = extension switch
        {
            ".png" => Starts(137, 80, 78, 71, 13, 10, 26, 10),
            ".jpg" or ".jpeg" => Starts(255, 216, 255),
            ".webp" => content.Length >= 12 && Starts(82, 73, 70, 70) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".pdf" => Starts(37, 80, 68, 70, 45),
            ".doc" or ".xls" => Starts(208, 207, 17, 224, 161, 177, 26, 225),
            ".docx" or ".xlsx" => Starts(80, 75, 3, 4),
            ".zip" => Starts(80, 75, 3, 4) || Starts(80, 75, 5, 6) || Starts(80, 75, 7, 8),
            _ => !content.Contains((byte)0)
        };
        if (!valid) throw new ArgumentException("El contenido del archivo no coincide con su formato.");
        return (fileName, extension, type!);
    }
}
