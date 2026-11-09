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

    public async Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken)
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = JsonContent.Create(new
            {
                model = settings.Model,
                input,
                tools,
                tool_choice = "auto",
                parallel_tool_calls = false,
                max_output_tokens = 1200,
                store = false
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AiProviderUnavailableException("El proveedor no pudo completar la consulta. Revisa la configuración y los límites de la API.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
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
            var inputTokens = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("input_tokens", out var inputTokenValue)
                && inputTokenValue.TryGetInt32(out var parsedInputTokens) ? parsedInputTokens : 0;
            var outputTokens = root.TryGetProperty("usage", out usage) && usage.TryGetProperty("output_tokens", out var outputTokenValue)
                && outputTokenValue.TryGetInt32(out var parsedOutputTokens) ? parsedOutputTokens : 0;

            return new AiModelResponseDto { Status = status, OutputText = outputText, OutputItems = output, InputTokens = inputTokens, OutputTokens = outputTokens };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderUnavailableException("El proveedor tardó demasiado en responder.");
        }
        catch (HttpRequestException)
        {
            throw new AiProviderUnavailableException("No se pudo establecer conexión con el proveedor.");
        }
        catch (JsonException)
        {
            throw new AiProviderUnavailableException("El proveedor devolvió una respuesta inválida.");
        }
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
        && settings.LiveDataApprovedModel == settings.Model;
}
