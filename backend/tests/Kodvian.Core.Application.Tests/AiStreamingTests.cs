using System.Text;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

/// <summary>Streaming de respuestas, eventos de progreso y sugerencias de seguimiento.</summary>
public sealed class AiStreamingTests
{
    [Fact]
    public async Task OpenAiStreamSendsDeltasAndParsesTheCompletedResponse()
    {
        const string body = """
        event: response.created
        data: {"type":"response.created","response":{"status":"in_progress"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","delta":"Vencen "}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","delta":"3 tareas."}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1","status":"completed","output":[{"id":"msg_1","type":"message","role":"assistant","content":[{"type":"output_text","text":"Vencen 3 tareas."}]}],"usage":{"input_tokens":40,"output_tokens":6}}}

        """;
        var deltas = new List<string>();

        var response = await OpenAiResponsesClient.ReadStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)),
            (text, _) => { deltas.Add(text); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(["Vencen ", "3 tareas."], deltas);
        Assert.Equal("Vencen 3 tareas.", response.OutputText);
        Assert.Equal("completed", response.Status);
        Assert.Equal(40, response.InputTokens);
        Assert.False(response.OutputItems[0]!.AsObject().ContainsKey("id"));
    }

    [Fact]
    public async Task OpenAiStreamThatEndsWithoutCompletionIsReportedAsUnavailable()
    {
        const string body = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hola\"}\n\n";
        await Assert.ThrowsAsync<AiProviderUnavailableException>(() => OpenAiResponsesClient.ReadStreamAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(body)), (_, _) => Task.CompletedTask, CancellationToken.None));
    }

    [Theory]
    [InlineData("Listo.\n[[sugerencias: Ver cobros pendientes | ¿Quién me debe? | Comparar con octubre]]", "Listo.", 3)]
    [InlineData("Sin sugerencias.", "Sin sugerencias.", 0)]
    [InlineData("Texto [[SUGERENCIAS: Uno | Uno | | Dos | Tres | Cuatro]]", "Texto", 3)]
    public void SuggestionsAreSplitFromTheVisibleText(string input, string expectedText, int expectedCount)
    {
        var (text, suggestions) = AiSuggestionParser.Extract(input);

        Assert.Equal(expectedText, text);
        Assert.Equal(expectedCount, suggestions.Count);
        Assert.Equal(suggestions.Count, suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task ObserverReceivesProgressAndSuggestionsAreStoredApart()
    {
        await using var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        var service = new AiConversationService(db, new ApprovedProvider(), new StreamingModel(), new ReadTool(), new ActiveSession(), NullLogger<AiConversationService>.Instance);
        var actor = new AiExecutionContext
        {
            UserId = Guid.NewGuid(), SessionVersion = Guid.NewGuid(),
            Roles = new HashSet<string>([RoleNames.Administrator]), Permissions = new HashSet<string>([PermissionCodes.AiOfficeUse])
        };
        var conversation = await service.CreateAsync(actor, new AiConversationCreateRequest(), CancellationToken.None);
        var observer = new RecordingObserver();

        var result = await service.SendMessageAsync(actor, conversation.Id, new AiSendMessageRequest { ClientRequestId = Guid.NewGuid(), Content = "¿Qué vence?" }, observer, CancellationToken.None);

        Assert.Equal(
            ["status:thinking", "delta:Reviso…", "reset", "status:tool:Consultando tareas…", "status:thinking", "delta:Vencen 3 tareas.", "delta:\n[[sugerencias: Ver detalle | Pasar a finalizada]]"],
            observer.Events);
        Assert.Equal("Vencen 3 tareas.", result.AssistantMessage.Content);
        Assert.Equal(["Ver detalle", "Pasar a finalizada"], result.AssistantMessage.Suggestions);
        var detail = await service.GetByIdAsync(actor, conversation.Id, CancellationToken.None);
        Assert.Equal(2, detail!.Messages.Single(x => x.Role == "assistant").Suggestions.Count);
    }

    private sealed class RecordingObserver : IAiRunObserver
    {
        public List<string> Events { get; } = [];
        public Task OnStatusAsync(string status, string label, string? toolName, CancellationToken cancellationToken)
        {
            Events.Add(toolName is null ? $"status:{status}" : $"status:{status}:{label}");
            return Task.CompletedTask;
        }
        public Task OnTextDeltaAsync(string text, CancellationToken cancellationToken) { Events.Add($"delta:{text}"); return Task.CompletedTask; }
        public Task OnTextResetAsync(CancellationToken cancellationToken) { Events.Add("reset"); return Task.CompletedTask; }
    }

    private sealed class StreamingModel : IAiResponsesClient
    {
        private int _calls;
        public Task<AiModelResponseDto> CreateAsync(Guid v, JsonArray input, JsonArray tools, CancellationToken ct) => throw new InvalidOperationException("Se esperaba streaming.");

        public async Task<AiModelResponseDto> CreateStreamingAsync(Guid v, JsonArray input, JsonArray tools, Func<string, CancellationToken, Task> onTextDelta, CancellationToken ct)
        {
            if (++_calls == 1)
            {
                await onTextDelta("Reviso…", ct);
                return new AiModelResponseDto
                {
                    Status = "completed", OutputText = "Reviso…",
                    OutputItems = new JsonArray(new JsonObject { ["type"] = "function_call", ["name"] = "tasks__list", ["call_id"] = "c1", ["arguments"] = "{}" })
                };
            }
            await onTextDelta("Vencen 3 tareas.", ct);
            await onTextDelta("\n[[sugerencias: Ver detalle | Pasar a finalizada]]", ct);
            return new AiModelResponseDto { Status = "completed", OutputText = "Vencen 3 tareas.\n[[sugerencias: Ver detalle | Pasar a finalizada]]" };
        }
    }

    private sealed class ReadTool : IAiToolExecutor
    {
        public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) =>
            [new AiToolDefinitionDto { Name = "tasks.list", Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() } }];
        public Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid v, string traceId, string toolName, JsonObject arguments, CancellationToken ct) =>
            Task.FromResult(new AiToolExecutionResultDto { Data = new JsonObject { ["count"] = 3 }, Source = new AiSourceReferenceDto { ToolName = toolName, Source = "test", AsOfUtc = DateTime.UtcNow } });
        public Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ActiveSession : ISessionValidator
    {
        public Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ApprovedProvider : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken c) => Task.FromResult(new AiProviderConfigurationDto { IsConfigured = true, IsConnectionVerified = true, LiveDataApproved = true, Provider = "Anthropic", Model = "m" });
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest r, CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest r, CancellationToken c) => throw new NotImplementedException();
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken c) => throw new NotImplementedException();
        public Task DeleteAsync(Guid v, CancellationToken c) => throw new NotImplementedException();
    }
}
