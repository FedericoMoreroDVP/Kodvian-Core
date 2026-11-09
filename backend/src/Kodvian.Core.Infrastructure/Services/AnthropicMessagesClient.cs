using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>
/// Cliente de Claude (Anthropic Messages API) para Oficina Inteligente.
/// La conversación se arma con el formato de items que usa el resto del módulo
/// (mensajes, "function_call" y "function_call_output"); este cliente lo traduce a
/// mensajes de Anthropic y devuelve la respuesta en ese mismo formato.
/// </summary>
public sealed class AnthropicMessagesClient(
    HttpClient httpClient,
    KodvianDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IAiResponsesClient
{
    internal const string ThinkingItemType = "anthropic_thinking";
    internal const string RedactedThinkingItemType = "anthropic_redacted_thinking";
    // Incluye el razonamiento del modelo además de la respuesta visible.
    private const int MaxOutputTokens = 16000;

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Kodvian.Core.AiProviderApiKey.v1");

    public async Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken)
    {
        var settings = await dbContext.AiProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null || settings.Provider != AiProviders.Anthropic || settings.Version != expectedConfigurationVersion
            || !settings.IsConnectionVerified || !AiProviderApproval.HasLiveDataApproval(settings))
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

        var (system, messages) = ToAnthropicMessages(input);
        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = MaxOutputTokens,
            System = system,
            Messages = messages,
            Tools = ToAnthropicTools(tools)
        };

        var client = AnthropicClientFactory.Create(httpClient, apiKey, TimeSpan.FromSeconds(120));
        Message response;
        try
        {
            response = await client.Messages.Create(parameters, cancellationToken: cancellationToken);
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new AiProviderUnavailableException("Anthropic rechazó la clave API. Revisá la configuración del proveedor.");
        }
        catch (AnthropicRateLimitException)
        {
            throw new AiProviderUnavailableException("Se alcanzó el límite de uso de la API de Anthropic. Intentá nuevamente en unos minutos.");
        }
        catch (AnthropicApiException)
        {
            throw new AiProviderUnavailableException("El proveedor no pudo completar la consulta. Revisa la configuración y los límites de la API.");
        }
        catch (AnthropicIOException)
        {
            throw new AiProviderUnavailableException("No se pudo establecer conexión con el proveedor.");
        }
        catch (HttpRequestException)
        {
            throw new AiProviderUnavailableException("No se pudo establecer conexión con el proveedor.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderUnavailableException("El proveedor tardó demasiado en responder.");
        }

        return FromAnthropicResponse(response);
    }

    internal static (string System, List<MessageParam> Messages) ToAnthropicMessages(JsonArray input)
    {
        var systemParts = new List<string>();
        var messages = new List<MessageParam>();
        string? currentRole = null;
        var currentContent = new List<ContentBlockParam>();

        void Append(string role, ContentBlockParam block)
        {
            if (currentRole != role) Flush();
            currentRole = role;
            currentContent.Add(block);
        }

        void Flush()
        {
            if (currentRole is null || currentContent.Count == 0) return;
            var content = currentContent;
            messages.Add(currentRole == "assistant"
                ? new AssistantMessageParam { Content = content }
                : new UserMessageParam { Content = content });
            currentRole = null;
            currentContent = [];
        }

        foreach (var item in input.OfType<JsonObject>())
        {
            var type = item["type"]?.GetValue<string>();
            switch (type)
            {
                case "function_call":
                    Append("assistant", new ToolUseBlockParam
                    {
                        ID = item["call_id"]?.GetValue<string>() ?? string.Empty,
                        Name = item["name"]?.GetValue<string>() ?? string.Empty,
                        Input = ParseArguments(item["arguments"]?.GetValue<string>())
                    });
                    continue;
                case "function_call_output":
                    Append("user", new ToolResultBlockParam
                    {
                        ToolUseID = item["call_id"]?.GetValue<string>() ?? string.Empty,
                        Content = item["output"]?.GetValue<string>() ?? string.Empty
                    });
                    continue;
                case ThinkingItemType:
                    // La firma tiene que volver sin cambios dentro del mismo ciclo de herramientas.
                    Append("assistant", new ThinkingBlockParam
                    {
                        Thinking = item["thinking"]?.GetValue<string>() ?? string.Empty,
                        Signature = item["signature"]?.GetValue<string>() ?? string.Empty
                    });
                    continue;
                case RedactedThinkingItemType:
                    Append("assistant", new RedactedThinkingBlockParam { Data = item["data"]?.GetValue<string>() ?? string.Empty });
                    continue;
            }

            var role = item["role"]?.GetValue<string>();
            var text = ReadText(item["content"]);
            if (role == "system")
            {
                if (!string.IsNullOrWhiteSpace(text)) systemParts.Add(text);
                continue;
            }
            if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(text))
                Append(role, new TextBlockParam { Text = text });
        }
        Flush();
        return (string.Join("\n\n", systemParts), messages);
    }

    internal static List<ToolUnion> ToAnthropicTools(JsonArray tools)
    {
        var result = new List<ToolUnion>();
        foreach (var tool in tools.OfType<JsonObject>())
        {
            var parameters = tool["parameters"] as JsonObject;
            var properties = new Dictionary<string, JsonElement>();
            if (parameters?["properties"] is JsonObject props)
                foreach (var (name, schema) in props)
                    properties[name] = JsonSerializer.SerializeToElement(schema);
            var required = parameters?["required"] is JsonArray req
                ? req.Select(x => x?.GetValue<string>()).OfType<string>().ToList()
                : [];

            result.Add(new Tool
            {
                Name = tool["name"]?.GetValue<string>() ?? string.Empty,
                Description = tool["description"]?.GetValue<string>(),
                InputSchema = new() { Properties = properties, Required = required }
            });
        }
        return result;
    }

    internal static AiModelResponseDto FromAnthropicResponse(Message response)
    {
        if (response.StopReason == "refusal")
            throw new AiProviderUnavailableException("Claude no respondió esta consulta por sus políticas de uso. Reformulala e intentá nuevamente.");

        var items = new JsonArray();
        var text = new System.Text.StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? textBlock))
            {
                text.Append(textBlock.Text);
                items.Add(new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = textBlock.Text })
                });
            }
            else if (block.TryPickThinking(out ThinkingBlock? thinking))
            {
                items.Add(new JsonObject { ["type"] = ThinkingItemType, ["thinking"] = thinking.Thinking, ["signature"] = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
            {
                items.Add(new JsonObject { ["type"] = RedactedThinkingItemType, ["data"] = redacted.Data });
            }
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
            {
                items.Add(new JsonObject
                {
                    ["type"] = "function_call",
                    ["call_id"] = toolUse.ID,
                    ["name"] = toolUse.Name,
                    ["arguments"] = JsonSerializer.Serialize(toolUse.Input)
                });
            }
        }

        var stopReason = response.StopReason?.ToString();
        var status = stopReason is "max_tokens" or "model_context_window_exceeded" ? "incomplete" : "completed";
        return new AiModelResponseDto
        {
            Status = status,
            OutputText = text.ToString(),
            OutputItems = items,
            InputTokens = (int)Math.Min(int.MaxValue, response.Usage.InputTokens),
            OutputTokens = (int)Math.Min(int.MaxValue, response.Usage.OutputTokens)
        };
    }

    private static Dictionary<string, JsonElement> ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ReadText(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var plain)) return plain;
        if (content is not JsonArray parts) return string.Empty;
        return string.Concat(parts.OfType<JsonObject>()
            .Select(x => x["text"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : string.Empty));
    }
}

/// <summary>Elige el cliente según el proveedor configurado.</summary>
public sealed class AiResponsesClientRouter(
    KodvianDbContext dbContext,
    OpenAiResponsesClient openAi,
    AnthropicMessagesClient anthropic) : IAiResponsesClient
{
    public async Task<AiModelResponseDto> CreateAsync(Guid expectedConfigurationVersion, JsonArray input, JsonArray tools, CancellationToken cancellationToken)
    {
        var provider = await dbContext.AiProviderConfigurations.AsNoTracking().Select(x => x.Provider).SingleOrDefaultAsync(cancellationToken);
        IAiResponsesClient client = provider switch
        {
            AiProviders.OpenAI => openAi,
            AiProviders.Anthropic => anthropic,
            _ => throw new AiProviderUnavailableException("El proveedor configurado no está disponible.")
        };
        return await client.CreateAsync(expectedConfigurationVersion, input, tools, cancellationToken);
    }
}

internal static class AiProviderApproval
{
    public static bool HasLiveDataApproval(Kodvian.Core.Domain.Entities.AiProviderConfiguration settings) =>
        settings.LiveDataApprovalTermsVersion == AiPrivacyPolicy.CurrentTermsVersion
        && settings.LiveDataApprovedByUserId.HasValue
        && settings.LiveDataApprovedAtUtc.HasValue
        && settings.LiveDataApprovedProvider == settings.Provider
        && settings.LiveDataApprovedModel == settings.Model;
}
