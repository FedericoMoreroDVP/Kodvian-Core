using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Kodvian.Core.Api.Controllers;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kodvian.Core.Application.Tests;

public sealed class AiConversationsApiTests : IDisposable
{
    private const string Issuer = "ai-conversation-tests";
    private const string Audience = "ai-conversation-tests";
    private const string Key = "a-long-enough-test-signing-key-000000000000";
    private readonly TestServer server;
    private readonly HttpClient client;

    public AiConversationsApiTests()
    {
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IAiConversationService, FakeAiConversationService>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = Issuer,
                    ValidateAudience = true, ValidAudience = Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
                };
            });
            services.AddAuthorization(options => options.AddPolicy("AiOfficeUse", policy =>
                policy.RequireRole(RoleNames.Administrator).RequireClaim(CustomClaimTypes.Permission, PermissionCodes.AiOfficeUse)));
            services.AddControllers().AddApplicationPart(typeof(AiConversationsController).Assembly);
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
        client = server.CreateClient();
    }

    [Fact]
    public async Task ConversationEndpointsRequireAdministratorAndAiPermission()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai/conversations")).StatusCode);

        Authenticate(Token(RoleNames.Administrator));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai/conversations")).StatusCode);

        Authenticate(Token(RoleNames.Administrator, PermissionCodes.AiOfficeUse));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai/conversations")).StatusCode);
    }

    [Fact]
    public async Task ActionEndpointsRequireAdministratorAndAiPermission()
    {
        var confirmUrl = $"/api/ai/conversations/{Guid.NewGuid()}/actions/{Guid.NewGuid()}/confirm";
        var cancelUrl = $"/api/ai/conversations/{Guid.NewGuid()}/actions/{Guid.NewGuid()}/cancel";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(confirmUrl, null)).StatusCode);

        Authenticate(Token(RoleNames.Administrator));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(confirmUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(cancelUrl, null)).StatusCode);

        Authenticate(Token(RoleNames.Administrator, PermissionCodes.AiOfficeUse));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(confirmUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(cancelUrl, null)).StatusCode);
    }

    [Fact]
    public async Task StreamEndpointAnswersWithServerSentEvents()
    {
        Authenticate(Token(RoleNames.Administrator, PermissionCodes.AiOfficeUse));
        var content = new StringContent($"{{\"clientRequestId\":\"{Guid.NewGuid()}\",\"content\":\"Hola\"}}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/ai/conversations/{Guid.NewGuid()}/messages/stream", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("event: status", body);
        Assert.Contains("event: delta", body);
        Assert.Contains("event: done", body);
        Assert.Contains("\"runId\"", body);
    }

    private void Authenticate(string token) => client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    private static string Token(string role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(CustomClaimTypes.SessionVersion, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role)
        };
        claims.AddRange(permissions.Select(x => new Claim(CustomClaimTypes.Permission, x)));
        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        client.Dispose();
        server.Dispose();
    }

    private sealed class FakeAiConversationService : IAiConversationService
    {
        public Task<AiConversationDto> CreateAsync(AiExecutionContext actor, AiConversationCreateRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiConversationDto());
        public Task<PagedResultDto<AiConversationDto>> GetPagedAsync(AiExecutionContext actor, PagedRequestDto request, CancellationToken cancellationToken) => Task.FromResult(new PagedResultDto<AiConversationDto>());
        public Task<AiConversationDetailDto?> GetByIdAsync(AiExecutionContext actor, Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<AiConversationDetailDto?>(null);
        public Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiSendMessageResultDto());
        public async Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, IAiRunObserver? observer, CancellationToken cancellationToken)
        {
            if (observer is not null)
            {
                await observer.OnStatusAsync("thinking", "Pensando…", null, cancellationToken);
                await observer.OnTextDeltaAsync("Hola", cancellationToken);
            }
            return new AiSendMessageResultDto { RunId = Guid.NewGuid() };
        }
        public Task<AiActionResolutionDto> ConfirmActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken) => Task.FromResult(new AiActionResolutionDto());
        public Task<AiActionResolutionDto> CancelActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken) => Task.FromResult(new AiActionResolutionDto());
        public Task<AiActionBatchResolutionDto> ConfirmAllActionsAsync(AiExecutionContext actor, Guid conversationId, Guid messageId, CancellationToken cancellationToken) => Task.FromResult(new AiActionBatchResolutionDto());
        public Task<AiActionResolutionDto> UndoActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken) => Task.FromResult(new AiActionResolutionDto());
        public Task<bool> DeleteAsync(AiExecutionContext actor, Guid conversationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
