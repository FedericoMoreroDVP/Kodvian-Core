using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Application.Tests;

public sealed class AiProviderConfigurationTests
{
    private const string ApiKey = "sk-test-secret-do-not-return";

    [Fact]
    public async Task SavesCredentialEncryptedAndNeverReturnsIt()
    {
        await using var database = CreateDatabase();
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = CreateService(database, dataProtection, new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var saved = await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "model-test", ApiKey = ApiKey, Version = Guid.Empty
        }, CancellationToken.None);

        var persisted = await database.AiProviderConfigurations.SingleAsync();
        Assert.NotEqual(ApiKey, persisted.ProtectedApiKey);
        Assert.Equal(ApiKey, dataProtection.CreateProtector("Kodvian.Core.AiProviderApiKey.v1").Unprotect(persisted.ProtectedApiKey));
        Assert.Equal("••••turn", saved.ApiKeyHint);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(saved));
        Assert.DoesNotContain(ApiKey, persisted.ApiKeyLastFour);
        Assert.Equal("configured", (await database.AiProviderConfigurationAuditEvents.SingleAsync()).Action);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(await database.AiProviderConfigurationAuditEvents.ToListAsync()));
    }

    [Fact]
    public async Task ConnectionTestUsesStoredKeyAndSelectedModelWithoutSendingBusinessData()
    {
        await using var database = CreateDatabase();
        string? authorization = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            authorization = request.Headers.Authorization?.ToString();
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(database, new EphemeralDataProtectionProvider(), handler);
        await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "model-test", ApiKey = ApiKey, Version = Guid.Empty
        }, CancellationToken.None);

        var result = await service.TestConnectionAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal($"Bearer {ApiKey}", authorization);
        Assert.Contains("model-test", body);
        Assert.Contains("store", body);
        Assert.DoesNotContain("Kodvian", body);
        Assert.True((await database.AiProviderConfigurations.SingleAsync()).IsConnectionVerified);
        Assert.Equal("connection_tested", (await database.AiProviderConfigurationAuditEvents.OrderBy(x => x.CreatedAtUtc).LastAsync()).Action);
    }

    [Fact]
    public async Task AnthropicConnectionTestChecksTheModelWithTheStoredKeyWithoutGeneratingText()
    {
        await using var database = CreateDatabase();
        string? apiKeyHeader = null;
        HttpMethod? method = null;
        string? path = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            apiKeyHeader = request.Headers.Authorization?.ToString();
            method = request.Method;
            path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"model","id":"claude-opus-5-5","display_name":"Claude Opus 5.5","created_at":"2026-01-01T00:00:00Z"}""", Encoding.UTF8, "application/json")
            });
        });
        var service = CreateService(database, new EphemeralDataProtectionProvider(), handler);
        await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "Anthropic", Model = "claude-opus-5-5", ApiKey = "sk-ant-test-key", Version = Guid.Empty
        }, CancellationToken.None);

        var result = await service.TestConnectionAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Bearer sk-ant-test-key", apiKeyHeader);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/v1/models/claude-opus-5-5", path);
    }

    [Fact]
    public async Task AnthropicConnectionTestExplainsARejectedKey()
    {
        await using var database = CreateDatabase();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", Encoding.UTF8, "application/json")
        }));
        var service = CreateService(database, new EphemeralDataProtectionProvider(), handler);
        await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "Anthropic", Model = "claude-opus-5-5", ApiKey = "sk-ant-wrong", Version = Guid.Empty
        }, CancellationToken.None);

        var result = await service.TestConnectionAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("clave", result.Message);
        Assert.False((await database.AiProviderConfigurations.SingleAsync()).IsConnectionVerified);
    }

    [Fact]
    public async Task RejectsUnsupportedProviderAndStaleConfigurationVersion()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database, new EphemeralDataProtectionProvider(), new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "Other", Model = "model-test", ApiKey = ApiKey, Version = Guid.Empty
        }, CancellationToken.None));

        var saved = await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "model-test", ApiKey = ApiKey, Version = Guid.Empty
        }, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "model-test-2", ApiKey = string.Empty, Version = Guid.Empty
        }, CancellationToken.None));
        Assert.NotEqual(Guid.Empty, saved.Version);
    }

    [Fact]
    public async Task SwitchingProviderRequiresANewKeyAndResetsVerification()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database, new EphemeralDataProtectionProvider(), new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var saved = await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "model-test", ApiKey = ApiKey, Version = Guid.Empty
        }, CancellationToken.None);
        await service.TestConnectionAsync(CancellationToken.None);
        saved = await service.GetAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "Anthropic", Model = "claude-opus-5-5", ApiKey = string.Empty, Version = saved.Version
        }, CancellationToken.None));

        var switched = await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "Anthropic", Model = "claude-opus-5-5", ApiKey = "sk-ant-test-key", Version = saved.Version
        }, CancellationToken.None);
        Assert.Equal("Anthropic", switched.Provider);
        Assert.False(switched.IsConnectionVerified);
        Assert.False(switched.LiveDataApproved);
    }

    [Fact]
    public async Task LiveDataApprovalRequiresVerifiedConnectionAndIsResetWhenCredentialsChange()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database, new EphemeralDataProtectionProvider(), new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "model-test", ApiKey = ApiKey, Version = Guid.Empty
        }, CancellationToken.None);
        var approval = new AiLiveDataApprovalRequest { AcknowledgeTerms = true, TermsVersion = Kodvian.Core.Application.Ai.AiPrivacyPolicy.CurrentTermsVersion };
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveLiveDataAsync(approval, CancellationToken.None));

        await service.TestConnectionAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveLiveDataAsync(new AiLiveDataApprovalRequest(), CancellationToken.None));
        var approved = await service.ApproveLiveDataAsync(approval, CancellationToken.None);
        Assert.True(approved.LiveDataApproved);
        Assert.NotNull(approved.LiveDataApprovedAtUtc);
        Assert.Contains(await database.AiProviderConfigurationAuditEvents.ToListAsync(), x => x.Action == "live_data_approved" && x.TermsVersion == Kodvian.Core.Application.Ai.AiPrivacyPolicy.CurrentTermsVersion);

        var changed = await service.UpdateAsync(new AiProviderConfigurationUpdateRequest
        {
            Provider = "OpenAI", Model = "other-model", ApiKey = string.Empty, Version = approved.Version
        }, CancellationToken.None);
        Assert.False(changed.LiveDataApproved);
    }

    private static KodvianDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KodvianDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new KodvianDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AiProviderConfigurationService CreateService(
        KodvianDbContext database, IDataProtectionProvider dataProtection, HttpMessageHandler handler) =>
        new(database, dataProtection,
        [
            new OpenAiConnectionTesterAdapter(new OpenAiApiConnectionTester(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") })),
            new AnthropicConnectionTester(new HttpClient(handler))
        ], new TestCurrentUser());

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? SessionVersion { get; } = Guid.NewGuid();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
