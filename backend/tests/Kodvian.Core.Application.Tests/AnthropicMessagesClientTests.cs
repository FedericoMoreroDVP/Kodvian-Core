using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Application.Tests;

public sealed class AnthropicMessagesClientTests
{
    [Fact]
    public async Task ToolCallResponseIsReturnedInTheModuleFormatKeepingThinking()
    {
        const string body = """
        {
          "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": [
            { "type": "thinking", "thinking": "", "signature": "sig-1" },
            { "type": "text", "text": "Reviso las tareas." },
            { "type": "tool_use", "id": "toolu_1", "name": "tasks__list", "input": { "days": 7 } }
          ],
          "stop_reason": "tool_use", "stop_sequence": null,
          "usage": { "input_tokens": 120, "output_tokens": 40 }
        }
        """;
        var (client, version, handler) = await CreateClientAsync(body);

        var response = await client.CreateAsync(version, BasicInput(), Tools(), CancellationToken.None);

        Assert.Equal("completed", response.Status);
        Assert.Equal("Reviso las tareas.", response.OutputText);
        Assert.Equal(120, response.InputTokens);
        Assert.Equal(40, response.OutputTokens);
        var items = response.OutputItems.OfType<JsonObject>().ToArray();
        Assert.Equal("anthropic_thinking", items[0]["type"]!.GetValue<string>());
        Assert.Equal("sig-1", items[0]["signature"]!.GetValue<string>());
        var call = items.Single(x => x["type"]!.GetValue<string>() == "function_call");
        Assert.Equal("toolu_1", call["call_id"]!.GetValue<string>());
        Assert.Equal("tasks__list", call["name"]!.GetValue<string>());
        Assert.Equal(7, JsonNode.Parse(call["arguments"]!.GetValue<string>())!["days"]!.GetValue<int>());

        using var request = JsonDocument.Parse(handler.LastBody!);
        var root = request.RootElement;
        Assert.Equal("Bearer test-key", handler.LastApiKey);
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Contains("Sos el asistente", root.GetProperty("system").ToString());
        Assert.Equal("tasks__list", root.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.True(root.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("properties").TryGetProperty("days", out _));
        Assert.False(root.TryGetProperty("thinking", out _));
        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ToolLoopIsSentBackAsToolUseAndToolResultWithTheThinkingSignature()
    {
        const string body = """
        {
          "id": "msg_2", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": [ { "type": "text", "text": "Vencen 3 tareas." } ],
          "stop_reason": "end_turn", "stop_sequence": null,
          "usage": { "input_tokens": 200, "output_tokens": 20 }
        }
        """;
        var (client, version, handler) = await CreateClientAsync(body);
        var input = BasicInput();
        input.Add(new JsonObject { ["type"] = "anthropic_thinking", ["thinking"] = "", ["signature"] = "sig-1" });
        input.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = "toolu_1", ["name"] = "tasks__list", ["arguments"] = "{\"days\":7}" });
        input.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = "toolu_1", ["output"] = "{\"count\":3}" });

        var response = await client.CreateAsync(version, input, Tools(), CancellationToken.None);

        Assert.Equal("Vencen 3 tareas.", response.OutputText);
        using var request = JsonDocument.Parse(handler.LastBody!);
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        var assistant = messages[1];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal("thinking", assistant.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("sig-1", assistant.GetProperty("content")[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", assistant.GetProperty("content")[1].GetProperty("type").GetString());
        Assert.Equal(7, assistant.GetProperty("content")[1].GetProperty("input").GetProperty("days").GetInt32());
        var result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
        Assert.False(result.TryGetProperty("is_error", out var isError) && isError.GetBoolean());
        Assert.True(request.RootElement.TryGetProperty("cache_control", out _));
    }

    [Fact]
    public async Task ToolErrorIsSentAsToolResultWithIsError()
    {
        const string body = """
        {
          "id": "msg_3", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": [ { "type": "text", "text": "Ese proyecto no existe." } ],
          "stop_reason": "end_turn", "stop_sequence": null,
          "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;
        var (client, version, handler) = await CreateClientAsync(body);
        var input = BasicInput();
        input.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = "toolu_9", ["name"] = "tasks__list", ["arguments"] = "{}" });
        input.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = "toolu_9", ["output"] = "{\"error\":\"El proyecto seleccionado no existe\"}" });

        await client.CreateAsync(version, input, Tools(), CancellationToken.None);

        using var request = JsonDocument.Parse(handler.LastBody!);
        var result = request.RootElement.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.True(result.GetProperty("is_error").GetBoolean());
    }

    [Theory]
    [InlineData("{\"error\":\"falló\"}", true)]
    [InlineData("{\"result\":[],\"error\":null,\"traceId\":\"x\"}", false)]
    [InlineData("{\"status\":\"pending_confirmation\"}", false)]
    [InlineData("texto", false)]
    public void ToolErrorDetectionOnlyMatchesTheErrorEnvelope(string output, bool expected) =>
        Assert.Equal(expected, AnthropicMessagesClient.IsToolError(output));

    [Fact]
    public async Task RefusalIsReportedAsProviderUnavailable()
    {
        const string body = """
        {
          "id": "msg_3", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": [], "stop_reason": "refusal", "stop_sequence": null,
          "usage": { "input_tokens": 10, "output_tokens": 0 }
        }
        """;
        var (client, version, _) = await CreateClientAsync(body);

        await Assert.ThrowsAsync<AiProviderUnavailableException>(() => client.CreateAsync(version, BasicInput(), Tools(), CancellationToken.None));
    }

    [Fact]
    public async Task RefusesToRunWhenTheConfiguredProviderIsNotAnthropic()
    {
        var (client, version, handler) = await CreateClientAsync("{}", provider: AiProviders.OpenAI);

        await Assert.ThrowsAsync<AiProviderUnavailableException>(() => client.CreateAsync(version, BasicInput(), Tools(), CancellationToken.None));
        Assert.Null(handler.LastBody);
    }

    private static JsonArray BasicInput() =>
    [
        new JsonObject { ["role"] = "system", ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = "Sos el asistente de prueba." }) },
        new JsonObject { ["role"] = "user", ["content"] = "¿Qué tareas vencen esta semana?" }
    ];

    private static JsonArray Tools() =>
    [
        new JsonObject
        {
            ["type"] = "function",
            ["name"] = "tasks__list",
            ["description"] = "Lista tareas.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["days"] = new JsonObject { ["type"] = "integer" } },
                ["required"] = new JsonArray()
            }
        }
    ];

    private static async Task<(AnthropicMessagesClient Client, Guid Version, CapturingHandler Handler)> CreateClientAsync(string responseBody, string provider = AiProviders.Anthropic)
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var dataProtection = new EphemeralDataProtectionProvider();
        var settings = new AiProviderConfiguration
        {
            Provider = provider,
            Model = "claude-opus-5-5",
            ProtectedApiKey = dataProtection.CreateProtector("Kodvian.Core.AiProviderApiKey.v1").Protect("test-key"),
            ApiKeyLastFour = "-key",
            IsConnectionVerified = true,
            LiveDataApprovalTermsVersion = AiPrivacyPolicy.CurrentTermsVersion,
            LiveDataApprovedByUserId = Guid.NewGuid(),
            LiveDataApprovedAtUtc = DateTime.UtcNow,
            LiveDataApprovedProvider = provider,
            LiveDataApprovedModel = "claude-opus-5-5"
        };
        db.AiProviderConfigurations.Add(settings);
        await db.SaveChangesAsync();
        var handler = new CapturingHandler(responseBody);
        return (new AnthropicMessagesClient(new HttpClient(handler), db, dataProtection), settings.Version, handler);
    }

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            LastApiKey = request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
