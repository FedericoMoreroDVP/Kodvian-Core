using System.Text;
using Kodvian.Core.Api.Auth;
using Kodvian.Core.Api.Middleware;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Infrastructure.Extensions;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("JWT key is not configured or is too short.");
}

if (!isDevelopment && jwtKey.StartsWith("SET_", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("JWT key placeholder is not allowed outside development.");
}

if (string.IsNullOrWhiteSpace(jwtIssuer))
{
    throw new InvalidOperationException("JWT issuer is not configured.");
}

if (string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException("JWT audience is not configured.");
}

builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024;
});
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Kodvian Core API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingresa el token JWT: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("auth_token", out var token) && !string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = SessionTokenValidation.ValidateAsync
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdministrationRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.AdministrationRead));

    options.AddPolicy("DashboardRead", policy =>
        policy.RequireRole(RoleNames.Administrator).RequireClaim(CustomClaimTypes.Permission, PermissionCodes.DashboardRead));

    options.AddPolicy("ClientsRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ClientsRead));

    options.AddPolicy("ClientsWrite", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ClientsWrite));

    options.AddPolicy("ProjectsRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ProjectsRead));

    options.AddPolicy("ProjectsWrite", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ProjectsWrite));

    options.AddPolicy("TasksRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.TasksRead));

    options.AddPolicy("TasksWrite", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.TasksWrite));

    options.AddPolicy("TeamRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.TeamRead));

    options.AddPolicy("TeamWrite", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.TeamWrite));

    options.AddPolicy("AdministratorOnly", policy =>
        policy.RequireRole(RoleNames.Administrator));

    options.AddPolicy("FinancesRead", policy =>
        policy.RequireRole(RoleNames.Administrator).RequireClaim(CustomClaimTypes.Permission, PermissionCodes.FinancesRead));

    options.AddPolicy("FinancesWrite", policy =>
        policy.RequireRole(RoleNames.Administrator).RequireClaim(CustomClaimTypes.Permission, PermissionCodes.FinancesWrite));

    options.AddPolicy("ProjectsDocumentsRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ProjectsDocumentsRead));

    options.AddPolicy("ProjectsDocumentsWrite", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ProjectsDocumentsWrite));

    options.AddPolicy("ProjectsDocumentsDelete", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.ProjectsDocumentsDelete));

    options.AddPolicy("DeveloperWorkRead", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.DeveloperWorkRead));

    options.AddPolicy("DeveloperTasksStatusWrite", policy =>
        policy.RequireClaim(CustomClaimTypes.Permission, PermissionCodes.DeveloperTasksStatusWrite));
});

var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"];
if (!string.IsNullOrWhiteSpace(allowedOrigins))
{
    var origins = allowedOrigins
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("FrontendCors", policy =>
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("AuthLogin", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// TLS terminates at Railway's proxy; do not redirect HTTP->HTTPS inside the container.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

if (!string.IsNullOrWhiteSpace(allowedOrigins))
{
    app.UseCors("FrontendCors");
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com data:; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<KodvianDbContext>();
    await dbContext.Database.MigrateAsync();
}

await DbSeeder.SeedAdminUserAsync(app.Services, app.Configuration);
await DbSeeder.SeedFinancialCategoriesAsync(app.Services);

app.Run();
