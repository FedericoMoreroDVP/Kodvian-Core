namespace Kodvian.Core.Application.Ai;

public static class AiProviders
{
    public const string OpenAI = "OpenAI";
    public const string Anthropic = "Anthropic";

    public static readonly IReadOnlyCollection<string> All = [OpenAI, Anthropic];

    public static bool IsSupported(string? provider) =>
        provider is not null && All.Contains(provider, StringComparer.Ordinal);
}
