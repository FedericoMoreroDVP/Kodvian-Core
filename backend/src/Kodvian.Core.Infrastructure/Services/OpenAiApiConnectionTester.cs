using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class OpenAiApiConnectionTester(HttpClient httpClient)
{
    public async Task<(bool IsSuccessful, string Message)> TestAsync(string apiKey, string model, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = JsonContent.Create(new
            {
                model,
                input = "Respond only with OK.",
                max_output_tokens = 8,
                store = false
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
                return (true, "La conexión con el proveedor y el modelo funciona.");

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                    (false, "El proveedor rechazó la clave o no tiene permisos para este modelo."),
                System.Net.HttpStatusCode.TooManyRequests =>
                    (false, "El proveedor alcanzó un límite de uso. Intenta nuevamente más tarde."),
                _ => (false, "No se pudo validar la conexión con el proveedor y el modelo.")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "El proveedor tardó demasiado en responder.");
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo establecer conexión con el proveedor.");
        }
    }
}
