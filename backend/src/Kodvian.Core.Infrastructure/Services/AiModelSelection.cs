using System.Text.RegularExpressions;

namespace Kodvian.Core.Infrastructure.Services;

public enum AiModelTier
{
    /// <summary>Modelo principal (el más potente): acciones, análisis y consultas largas.</summary>
    Main,
    /// <summary>Modelo rápido opcional: consultas simples.</summary>
    Fast
}

/// <summary>
/// Elige qué modelo usar en cada ejecución y lo deja disponible para los clientes de los proveedores
/// sin cambiar la firma de IAiResponsesClient (vale para el flujo asíncrono actual).
/// </summary>
public static partial class AiModelSelection
{
    private static readonly AsyncLocal<AiModelTier?> CurrentTier = new();

    public static AiModelTier Current => CurrentTier.Value ?? AiModelTier.Main;

    public static IDisposable Use(AiModelTier tier)
    {
        var previous = CurrentTier.Value;
        CurrentTier.Value = tier;
        return new Restore(() => CurrentTier.Value = previous);
    }

    /// <summary>Modelo efectivo: el rápido solo si está configurado y la ejecución lo pidió.</summary>
    public static string Resolve(string model, string? fastModel) =>
        Current == AiModelTier.Fast && !string.IsNullOrWhiteSpace(fastModel) ? fastModel : model;

    // Pedidos de acción o de análisis: van al modelo principal, que se equivoca menos con proyectos, personas y fechas.
    [GeneratedRegex(@"\b(agend|cre[áa]|crea|program|reprogram|mov[eé]|cancel|registr|asign|reasign|pas[áa]|cambi|marc[áa]|guard|record[áa]|borr|elimin|confirm|compar|analiz|por qu[eé]|explic|proyect[áa]|estrateg|recomend|planific)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComplexIntent();

    public static AiModelTier Choose(string userMessage, bool fastModelConfigured)
    {
        if (!fastModelConfigured) return AiModelTier.Main;
        if (userMessage.Length > 300) return AiModelTier.Main;
        return ComplexIntent().IsMatch(userMessage) ? AiModelTier.Main : AiModelTier.Fast;
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
