using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Clients.Abstractions;
using Kodvian.Core.Application.Developers.Abstractions;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Dashboard.Abstractions;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Application.Locations.Abstractions;
using Kodvian.Core.Application.MyWork.Abstractions;
using Kodvian.Core.Application.Projects.Abstractions;
using Kodvian.Core.Application.Team.Abstractions;
using Kodvian.Core.Application.Tasks.Abstractions;
using Kodvian.Core.Application.Meetings.Abstractions;
using Kodvian.Core.Infrastructure.Auth;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Storage;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Kodvian.Core.Infrastructure.Extensions;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddDbContext<KodvianDbContext>(options =>
            options.UseNpgsql(BuildConnectionString(configuration)));

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Kodvian.Core")
            .PersistKeysToDbContext<KodvianDbContext>();
        var certificateValue = configuration["AiEncryption:CertificateBase64"];
        var certificatePassword = configuration["AiEncryption:CertificatePassword"];
        if (string.IsNullOrWhiteSpace(certificateValue))
        {
            if (!isDevelopment)
                throw new InvalidOperationException("AiEncryption:CertificateBase64 is required outside Development.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(certificatePassword))
                throw new InvalidOperationException("AiEncryption:CertificatePassword is required when AiEncryption:CertificateBase64 is configured.");

            byte[] certificateBytes;
            try
            {
                certificateBytes = Convert.FromBase64String(certificateValue);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("AiEncryption:CertificateBase64 is not valid Base64.", exception);
            }

            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(certificateBytes, certificatePassword,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException(
                    "AiEncryption:CertificateBase64 could not be opened with AiEncryption:CertificatePassword.", exception);
            }

            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("AiEncryption:CertificateBase64 must contain a PFX certificate with a private key.");

            dataProtection.ProtectKeysWithCertificate(certificate);
        }

        services.AddHttpClient<OpenAiApiConnectionTester>(client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<OpenAiResponsesClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        // El SDK de Anthropic aplica su propio timeout por solicitud; el HttpClient solo pone un tope externo.
        services.AddHttpClient<AnthropicMessagesClient>(client => client.Timeout = TimeSpan.FromSeconds(150))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<AnthropicConnectionTester>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiResponsesClient, AiResponsesClientRouter>();
        services.AddScoped<IAiProviderConnectionTester, OpenAiConnectionTesterAdapter>();
        services.AddScoped<IAiProviderConnectionTester>(sp => sp.GetRequiredService<AnthropicConnectionTester>());

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        RegisterFileStorage(services, configuration);

        services.AddScoped<IPasswordHasher, PasswordHasherService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISessionValidator, SessionValidator>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IDeveloperService, DeveloperService>();
        services.AddScoped<IProjectDeveloperAssignmentService, ProjectDeveloperAssignmentService>();
        services.AddScoped<IProjectDeveloperContractService, ProjectDeveloperContractService>();
        services.AddScoped<IDeveloperPaymentService, DeveloperPaymentService>();
        services.AddScoped<DeveloperPaymentAccountingService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IFinancialCategoryService, FinancialCategoryService>();
        services.AddScoped<IFinancialMovementService, FinancialMovementService>();
        services.AddScoped<IFinanceOverviewService, FinanceOverviewService>();
        services.AddScoped<IProviderService, ProviderService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IMyWorkService, MyWorkService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITeamUserService, TeamUserService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiProviderConfigurationService, AiProviderConfigurationService>();
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiConversationService, AiConversationService>();
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiToolExecutor, AiToolCatalog>();
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiBriefingService, AiBriefingService>();
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiPreferencesService, AiPreferencesService>();
        services.AddScoped<Kodvian.Core.Application.Ai.Abstractions.IAiUsageService, AiUsageService>();
        services.AddHostedService<AiAuditRetentionWorker>();
        services.AddScoped<IMeetingService, MeetingService>();
        services.AddScoped<ITaskAttachmentService, TaskAttachmentService>();
        services.AddScoped<UserAccessGuard>();
        services.AddScoped<Kodvian.Core.Application.Administration.IUserAdministrationService, UserAdministrationService>();

        return services;
    }

    private static void RegisterFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        var storageSection = configuration.GetSection("Storage");
        var provider = storageSection["Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = StorageOptions.LocalProvider;
        }

        if (string.Equals(provider, StorageOptions.S3Provider, StringComparison.OrdinalIgnoreCase))
        {
            ValidateS3Options(storageSection);
            services.AddSingleton<IFileStorageService, S3FileStorageService>();
            return;
        }

        if (!string.Equals(provider, StorageOptions.LocalProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported storage provider '{provider}'. Use Local or S3.");
        }

        services.AddScoped<IFileStorageService, LocalFileStorageService>();
    }

    private static void ValidateS3Options(IConfigurationSection storageSection)
    {
        var bucket = storageSection["Bucket"];
        var accessKey = storageSection["AccessKey"];
        var secretKey = storageSection["SecretKey"];
        var serviceUrl = storageSection["ServiceUrl"];
        var region = storageSection["Region"];

        if (string.IsNullOrWhiteSpace(bucket)
            || string.IsNullOrWhiteSpace(accessKey)
            || string.IsNullOrWhiteSpace(secretKey)
            || (string.IsNullOrWhiteSpace(serviceUrl) && string.IsNullOrWhiteSpace(region)))
        {
            throw new InvalidOperationException(
                "S3 storage requires Bucket, AccessKey, SecretKey, and ServiceUrl or Region.");
        }
    }

    private static string BuildConnectionString(IConfiguration configuration)
    {
        var railwayDatabaseUrl = configuration["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(railwayDatabaseUrl))
        {
            var uri = new Uri(railwayDatabaseUrl);
            var userInfo = uri.UserInfo.Split(':', 2);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port,
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
                Database = uri.AbsolutePath.TrimStart('/'),
                SslMode = SslMode.Require,
                MaxPoolSize = 20,
                Timeout = 15,
                CommandTimeout = 30
            };

            return builder.ToString();
        }

        return configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string was not configured.");
    }
}
