using System.Text;
using System.Text.Json;
using Kodvian.Core.Application.Ai.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace Kodvian.Core.Api.Controllers;

/// <summary>
/// Escribe el progreso de una ejecución del asistente como server-sent events.
/// La respuesta HTTP empieza recién con el primer evento: los errores de validación previos
/// (conversación inexistente, consulta en curso, proveedor no aprobado) siguen saliendo como JSON normal.
/// </summary>
internal sealed class AiSseObserver(HttpResponse response) : IAiRunObserver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool Started { get; private set; }

    public Task OnStatusAsync(string status, string label, string? toolName, CancellationToken cancellationToken) =>
        WriteAsync("status", new { status, label, toolName }, cancellationToken);

    public Task OnTextDeltaAsync(string text, CancellationToken cancellationToken) =>
        WriteAsync("delta", new { text }, cancellationToken);

    public Task OnTextResetAsync(CancellationToken cancellationToken) =>
        WriteAsync("reset", new { }, cancellationToken);

    public async Task WriteAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!Started)
            {
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "text/event-stream; charset=utf-8";
                response.Headers.CacheControl = "no-cache, no-transform";
                response.Headers["X-Accel-Buffering"] = "no";
                response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
                Started = true;
            }
            var data = JsonSerializer.Serialize(payload, JsonOptions);
            await response.Body.WriteAsync(Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {data}\n\n"), cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}
