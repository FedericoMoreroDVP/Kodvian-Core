using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Extensions;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kodvian.Core.Application.Tests;

public sealed class OpenAiResponsesClientTests
{
    [Fact]
    public void InfrastructureResolvesTheRealResponsesClientWithItsHttpClient()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=kodvian_test;Username=test;Password=test"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<Kodvian.Core.Application.Common.Security.ICurrentUser, NoCurrentUser>();
        services.AddInfrastructure(configuration, isDevelopment: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<AiResponsesClientRouter>(scope.ServiceProvider.GetRequiredService<IAiResponsesClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AnthropicMessagesClient>());
        Assert.Equal(2, scope.ServiceProvider.GetServices<IAiProviderConnectionTester>().Select(x => x.Provider).Distinct().Count());
        var client = scope.ServiceProvider.GetRequiredService<OpenAiResponsesClient>();
        var httpClient = (HttpClient)typeof(OpenAiResponsesClient)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(x => x.FieldType == typeof(HttpClient))
            .GetValue(client)!;
        Assert.Equal(new Uri("https://api.openai.com/v1/"), httpClient.BaseAddress);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAiConversationService>());
    }

    [Fact]
    public async Task OutputTextIsReadFromMessageItemsBecauseTheRestApiDoesNotSendOutputText()
    {
        const string body = """
        {
          "status": "completed",
          "output": [
            { "type": "reasoning", "id": "rs_1", "summary": [] },
            { "type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
              "content": [
                { "type": "output_text", "text": "Hay 3 proyectos activos.", "annotations": [] },
                { "type": "output_text", "text": " Ninguno vencido.", "annotations": [] }
              ] }
          ],
          "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;
        var (client, version, _) = await CreateClientAsync(body);

        var response = await client.CreateAsync(version, [], [], CancellationToken.None);

        Assert.Equal("Hay 3 proyectos activos. Ninguno vencido.", response.OutputText);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
    }

    [Fact]
    public async Task OutputItemsAreSafeToSendBackWithStoreDisabled()
    {
        const string body = """
        {
          "status": "completed",
          "output": [
            { "type": "reasoning", "id": "rs_1", "summary": [] },
            { "type": "function_call", "id": "fc_1", "call_id": "call_1", "name": "projects.list", "arguments": "{}", "status": "completed" }
          ],
          "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;
        var (client, version, _) = await CreateClientAsync(body);

        var response = await client.CreateAsync(version, [], [], CancellationToken.None);

        var item = Assert.Single(response.OutputItems);
        var call = Assert.IsType<JsonObject>(item);
        Assert.Equal("function_call", call["type"]!.GetValue<string>());
        Assert.Equal("call_1", call["call_id"]!.GetValue<string>());
        Assert.False(call.ContainsKey("id"));
    }

    private static async Task<(OpenAiResponsesClient Client, Guid Version, StubHandler Handler)> CreateClientAsync(string responseBody)
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var dataProtection = new EphemeralDataProtectionProvider();
        var settings = new AiProviderConfiguration
        {
            Model = "test-model",
            ProtectedApiKey = dataProtection.CreateProtector("Kodvian.Core.AiProviderApiKey.v1").Protect("test-key"),
            ApiKeyLastFour = "-key",
            IsConnectionVerified = true,
            LiveDataApprovalTermsVersion = AiPrivacyPolicy.CurrentTermsVersion,
            LiveDataApprovedByUserId = Guid.NewGuid(),
            LiveDataApprovedAtUtc = DateTime.UtcNow,
            LiveDataApprovedProvider = "OpenAI",
            LiveDataApprovedModel = "test-model"
        };
        db.AiProviderConfigurations.Add(settings);
        await db.SaveChangesAsync();
        var handler = new StubHandler(responseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") };
        return (new OpenAiResponsesClient(http, db, dataProtection), settings.Version, handler);
    }

    private sealed class NoCurrentUser : Kodvian.Core.Application.Common.Security.ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? SessionVersion => null;
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
