using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class OpenAiResponsesClient(
    HttpClient httpClient,
    KodvianDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IAiResponsesClient
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Kodvian.Core.AiProviderApiKey.v1");

    public Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken) =>
        SendAsync(expectedConfigurationVersion, input, tools, null, cancellationToken);

    public Task<AiModelResponseDto> CreateStreamingAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, Func<string, CancellationToken, Task> onTextDelta, CancellationToken cancellationToken) =>
        SendAsync(expectedConfigurationVersion, input, tools, onTextDelta, cancellationToken);

    private async Task<AiModelResponseDto> SendAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, Func<string, CancellationToken, Task>? onTextDelta, CancellationToken cancellationToken)
    {
        var settings = await dbContext.AiProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null || settings.Provider != AiProviders.OpenAI || settings.Version != expectedConfigurationVersion || !settings.IsConnectionVerified || !HasLiveDataApproval(settings))
            throw new AiProviderUnavailableException("El proveedor no está aprobado para procesar datos reales.");

        string apiKey;
        try
        {
            apiKey = _protector.Unprotect(settings.ProtectedApiKey);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new AiProviderUnavailableException("No se pudo abrir de forma segura la configuración del proveedor.");
        }

        var streaming = onTextDelta is not null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = JsonContent.Create(new
            {
                model = AiModelSelection.Resolve(settings.Model, settings.FastModel),
                input,
                tools,
                tool_choice = "auto",
                parallel_tool_calls = false,
                max_output_tokens = 4000,
                store = false,
                stream = streaming
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (streaming) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AiProviderUnavailableException("El proveedor no pudo completar la consulta. Revisa la configuración y los límites de la API.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (!streaming)
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ParseResponse(document.RootElement);
            }
            return await ReadStreamAsync(stream, onTextDelta!, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderUnavailableException("El proveedor tardó demasiado en responder.");
        }
        catch (HttpRequestException)
        {
            throw new AiProviderUnavailableException("No se pudo establecer conexión con el proveedor.");
        }
        catch (IOException)
        {
            throw new AiProviderUnavailableException("Se cortó la conexión con el proveedor.");
        }
        catch (JsonException)
        {
            throw new AiProviderUnavailableException("El proveedor devolvió una respuesta inválida.");
        }
    }

    // Server-sent events de la Responses API: se transmiten los "response.output_text.delta" y la respuesta
    // completa llega en "response.completed" (o incomplete/failed) con el mismo formato que sin streaming.
    internal static async Task<AiModelResponseDto> ReadStreamAsync(Stream stream, Func<string, CancellationToken, Task> onTextDelta, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            switch (type)
            {
                case "response.output_text.delta":
                    if (root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String && delta.GetString() is { Length: > 0 } text)
                        await onTextDelta(text, cancellationToken);
                    break;
                case "response.completed":
                case "response.incomplete":
                case "response.failed":
                    if (root.TryGetProperty("response", out var final) && final.ValueKind == JsonValueKind.Object)
                        return ParseResponse(final);
                    break;
                case "error":
                    throw new AiProviderUnavailableException("El proveedor no pudo completar la consulta. Revisa la configuración y los límites de la API.");
            }
        }
        throw new AiProviderUnavailableException("El proveedor cortó la respuesta antes de terminar.");
    }

    internal static AiModelResponseDto ParseResponse(JsonElement root)
    {
        var rawOutput = root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array
            ? JsonNode.Parse(outputElement.GetRawText()) as JsonArray ?? []
            : [];
        // The REST API does not return "output_text" (it is an SDK helper), so the text comes from the message items.
        var outputText = root.TryGetProperty("output_text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : ExtractMessageText(rawOutput);
        var output = PrepareItemsForNextTurn(rawOutput);
        var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() ?? string.Empty
            : string.Empty;
        var inputTokens = root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var inputTokenValue)
            && inputTokenValue.TryGetInt32(out var parsedInputTokens) ? parsedInputTokens : 0;
        var outputTokens = root.TryGetProperty("usage", out usage) && usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var outputTokenValue)
            && outputTokenValue.TryGetInt32(out var parsedOutputTokens) ? parsedOutputTokens : 0;

        return new AiModelResponseDto { Status = status, OutputText = outputText, OutputItems = output, InputTokens = inputTokens, OutputTokens = outputTokens };
    }

    private static string ExtractMessageText(JsonArray output)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var item in output.OfType<JsonObject>())
        {
            if (!string.Equals(item["type"]?.GetValue<string>(), "message", StringComparison.Ordinal)
                || item["content"] is not JsonArray content)
                continue;
            foreach (var part in content.OfType<JsonObject>())
            {
                if (string.Equals(part["type"]?.GetValue<string>(), "output_text", StringComparison.Ordinal)
                    && part["text"] is JsonValue text && text.TryGetValue<string>(out var value))
                    builder.Append(value);
            }
        }
        return builder.ToString();
    }

    // With store = false the provider keeps no items, so sending back server ids (rs_/fc_/msg_) or reasoning
    // items without encrypted content makes the next call fail. Items are resent inline and without ids.
    private static JsonArray PrepareItemsForNextTurn(JsonArray output)
    {
        var items = new JsonArray();
        foreach (var item in output.OfType<JsonObject>())
        {
            if (string.Equals(item["type"]?.GetValue<string>(), "reasoning", StringComparison.Ordinal))
                continue;
            var copy = (JsonObject)item.DeepClone();
            copy.Remove("id");
            items.Add(copy);
        }
        return items;
    }

    private static bool HasLiveDataApproval(Kodvian.Core.Domain.Entities.AiProviderConfiguration settings) =>
        settings.LiveDataApprovalTermsVersion == AiPrivacyPolicy.CurrentTermsVersion
        && settings.LiveDataApprovedByUserId.HasValue
        && settings.LiveDataApprovedAtUtc.HasValue
        && settings.LiveDataApprovedProvider == settings.Provider
        && settings.LiveDataApprovedModel == settings.Model
        && settings.LiveDataApprovedFastModel == settings.FastModel;
}
