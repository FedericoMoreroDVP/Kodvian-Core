using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Kodvian.Core.Api.Auth;
using Kodvian.Core.Api.Controllers;
using Kodvian.Core.Api.Middleware;
using Kodvian.Core.Application.Administration;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Auth.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Auth;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kodvian.Core.Application.Tests;

public class UserRolesApiTests : IDisposable
{
    private readonly TestServer server;
    private readonly HttpClient client;
    private readonly JwtOptions jwt = new() { Key = new string('k', 64), Issuer = "tests", Audience = "tests", ExpirationMinutes = 10 };
    private readonly User admin;
    private readonly User target;

    public UserRolesApiTests()
    {
        var database = Guid.NewGuid().ToString();
        server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddDbContext<KodvianDbContext>(o => o.UseInMemoryDatabase(database));
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpCurrentUser>();
            services.AddScoped<ISessionValidator, SessionValidator>();
            services.AddScoped<UserAccessGuard>();
            services.AddScoped<IUserAdministrationService, UserAdministrationService>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = SessionTokenValidation.ValidateAsync };
            });
            services.AddAuthorization(o => o.AddPolicy("AdministratorOnly", p => p.RequireRole(RoleNames.Administrator)));
            services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
        }).Configure(app =>
        {
            app.UseMiddleware<ErrorHandlingMiddleware>();
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers());
        }));
        client = server.CreateClient();
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        db.Database.EnsureCreated();
        admin = CreateUser(db, "admin@example.test", RoleNames.Administrator, RoleNames.Developer);
        target = CreateUser(db, "target@example.test", RoleNames.Administrator, RoleNames.Analyst);
    }

    private static User CreateUser(KodvianDbContext db, string email, params string[] names)
    {
        var user = new User { FullName = "Persona", Email = email };
        foreach (var name in names) user.UserRoles.Add(new UserRole { Role = db.Roles.Single(r => r.Name == name) });
        db.Users.Add(user); db.SaveChanges(); return user;
    }
    private string Token(User user) => new TokenService(Options.Create(jwt)).GenerateToken(new TokenGenerationDto
    {
        UserId = user.Id, Email = user.Email, FullName = user.FullName, SessionVersion = user.SessionVersion,
        Roles = user.UserRoles.Select(x => x.Role.Name).ToArray(), Permissions = RolePermissionMap.GetPermissions(user.UserRoles.Select(x => x.Role.Name))
    }).AccessToken;
    private void Authenticate(string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task RequiresAuthenticationAndAdministratorRole()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        var developer = CreateUser(db, "dev@example.test", RoleNames.Developer);
        Authenticate(Token(developer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/users/{developer.Id}/roles", new { roles = new[] { RoleNames.Administrator }, expectedVersion = developer.SessionVersion })).StatusCode);
        Authenticate(Token(admin));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task RoleChangeInvalidatesAlreadyIssuedJwtAtNextRequest()
    {
        var oldToken = Token(target);
        Authenticate(oldToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/roles")).StatusCode);
        Authenticate(Token(admin));
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}/roles", new { roles = new[] { RoleNames.Developer }, expectedVersion = target.SessionVersion });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Authenticate(oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/roles")).StatusCode);
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
        var updated = await db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).SingleAsync(u => u.Id == target.Id);
        Authenticate(Token(updated));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/roles")).StatusCode);
    }

    public void Dispose() { client.Dispose(); server.Dispose(); }
}
