using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Kodvian.Core.Api.Controllers;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kodvian.Core.Application.Tests;

public sealed class AiProviderConfigurationApiTests : IDisposable
{
    private const string Issuer = "ai-config-tests";
    private const string Audience = "ai-config-tests";
    private const string Key = "a-long-enough-test-signing-key-000000000000";
    private readonly TestServer server;
    private readonly HttpClient client;

    public AiProviderConfigurationApiTests()
    {
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IAiProviderConfigurationService, FakeAiProviderConfigurationService>();
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
            services.AddAuthorization(options => options.AddPolicy("AdministratorOnly", policy => policy.RequireRole("Administrador")));
            services.AddControllers().AddApplicationPart(typeof(AiProviderConfigurationController).Assembly);
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
    public async Task ConfigurationEndpointIsAdministratorOnly()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai/provider-configuration")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken("Analista"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai/provider-configuration")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken("Administrador"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai/provider-configuration")).StatusCode);
    }

    private static string CreateToken(string role)
    {
        var token = new JwtSecurityToken(Issuer, Audience, [new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        client.Dispose();
        server.Dispose();
    }

    private sealed class FakeAiProviderConfigurationService : IAiProviderConfigurationService
    {
        public Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto());
        public Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto());
        public Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConnectionTestDto());
        public Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto());
        public Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderConfigurationDto());
        public Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
