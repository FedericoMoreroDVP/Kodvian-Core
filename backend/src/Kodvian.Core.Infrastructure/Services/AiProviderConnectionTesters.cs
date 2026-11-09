using Anthropic;
using Anthropic.Exceptions;
using Kodvian.Core.Application.Ai;

namespace Kodvian.Core.Infrastructure.Services;

public interface IAiProviderConnectionTester
{
    string Provider { get; }
    Task<(bool IsSuccessful, string Message)> TestAsync(string apiKey, string model, CancellationToken cancellationToken);
}

public sealed class OpenAiConnectionTesterAdapter(OpenAiApiConnectionTester tester) : IAiProviderConnectionTester
{
    public string Provider => AiProviders.OpenAI;

    public Task<(bool IsSuccessful, string Message)> TestAsync(string apiKey, string model, CancellationToken cancellationToken) =>
        tester.TestAsync(apiKey, model, cancellationToken);
}

/// <summary>
/// Valida la clave y el modelo de Anthropic consultando el modelo en la Models API, que no genera costo.
/// </summary>
public sealed class AnthropicConnectionTester(HttpClient httpClient) : IAiProviderConnectionTester
{
    public string Provider => AiProviders.Anthropic;

    public async Task<(bool IsSuccessful, string Message)> TestAsync(string apiKey, string model, CancellationToken cancellationToken)
    {
        var client = AnthropicClientFactory.Create(httpClient, apiKey, TimeSpan.FromSeconds(15));
        try
        {
            await client.Models.Retrieve(model, cancellationToken: cancellationToken);
            return (true, "La conexión con el proveedor y el modelo funciona.");
        }
        catch (AnthropicNotFoundException)
        {
            return (false, "La clave es válida, pero el modelo indicado no existe o no está disponible para esta cuenta.");
        }
        catch (AnthropicUnauthorizedException)
        {
            return (false, "El proveedor rechazó la clave API. Verificá que sea una clave de la Console de Anthropic y que esté activa.");
        }
        catch (AnthropicForbiddenException)
        {
            return (false, "La clave no tiene permisos para este modelo o workspace.");
        }
        catch (AnthropicRateLimitException)
        {
            return (false, "El proveedor alcanzó un límite de uso. Intenta nuevamente más tarde.");
        }
        catch (AnthropicApiException)
        {
            return (false, "No se pudo validar la conexión con el proveedor y el modelo.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "El proveedor tardó demasiado en responder.");
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo establecer conexión con el proveedor.");
        }
        catch (AnthropicIOException)
        {
            return (false, "No se pudo establecer conexión con el proveedor.");
        }
    }
}

internal static class AnthropicClientFactory
{
    // La clave sale siempre de la configuración cifrada; al pasarla explícita el SDK no busca
    // credenciales en variables de entorno ni perfiles locales.
    public static AnthropicClient Create(HttpClient httpClient, string apiKey, TimeSpan timeout) => new()
    {
        HttpClient = httpClient,
        ApiKey = apiKey,
        Timeout = timeout,
        MaxRetries = 1
    };
}
