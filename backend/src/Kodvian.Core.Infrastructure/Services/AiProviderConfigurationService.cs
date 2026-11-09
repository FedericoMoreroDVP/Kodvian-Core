using System.Text.RegularExpressions;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class AiProviderConfigurationService(
    KodvianDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IEnumerable<IAiProviderConnectionTester> connectionTesters,
    ICurrentUser currentUser) : IAiProviderConfigurationService
{
    private static readonly Regex ModelPattern = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Kodvian.Core.AiProviderApiKey.v1");

    public async Task<AiProviderConfigurationDto> GetAsync(CancellationToken cancellationToken)
    {
        var entity = await dbContext.AiProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<AiProviderConfigurationDto> UpdateAsync(AiProviderConfigurationUpdateRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var entity = await dbContext.AiProviderConfigurations.SingleOrDefaultAsync(cancellationToken);
        var isNew = entity is null;
        if (entity is null)
        {
            if (request.Version != Guid.Empty)
                throw new ArgumentException("La configuración cambió. Actualiza la página e intenta nuevamente.");
            if (string.IsNullOrWhiteSpace(request.ApiKey))
                throw new ArgumentException("Ingresa la clave API del proveedor.");

            entity = new AiProviderConfiguration();
            dbContext.AiProviderConfigurations.Add(entity);
        }
        else if (entity.Version != request.Version)
        {
            throw new ArgumentException("La configuración cambió. Actualiza la página e intenta nuevamente.");
        }

        var providerChanged = !isNew && entity.Provider != request.Provider;
        if (providerChanged && string.IsNullOrWhiteSpace(request.ApiKey))
            throw new ArgumentException("Ingresa la clave API del nuevo proveedor: la clave guardada pertenece a otro proveedor.");

        var changed = entity.Provider != request.Provider || entity.Model != request.Model;
        var credentialReplaced = false;
        entity.Provider = request.Provider;
        entity.Model = request.Model;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            var normalizedKey = request.ApiKey.Trim();
            credentialReplaced = string.IsNullOrWhiteSpace(entity.ProtectedApiKey)
                || !string.Equals(_protector.Unprotect(entity.ProtectedApiKey), normalizedKey, StringComparison.Ordinal);
            changed |= credentialReplaced;
            entity.ProtectedApiKey = _protector.Protect(normalizedKey);
            entity.ApiKeyLastFour = normalizedKey[^Math.Min(4, normalizedKey.Length)..];
        }
        else if (string.IsNullOrWhiteSpace(entity.ProtectedApiKey))
        {
            throw new ArgumentException("Ingresa la clave API del proveedor.");
        }

        if (changed)
        {
            entity.IsConnectionVerified = false;
            entity.LastConnectionTestAtUtc = null;
            ClearLiveDataApproval(entity);
        }
        entity.Version = Guid.NewGuid();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(isNew ? "configured" : "updated", entity.Provider, entity.Model, credentialReplaced, null);
        await SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<AiProviderConnectionTestDto> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var entity = await dbContext.AiProviderConfigurations.SingleOrDefaultAsync(cancellationToken)
            ?? throw new AiProviderUnavailableException("Configura primero el proveedor de IA.");

        var connectionTester = connectionTesters.SingleOrDefault(x => x.Provider == entity.Provider)
            ?? throw new AiProviderUnavailableException("El proveedor configurado no está disponible.");
        var (isSuccessful, message) = await connectionTester.TestAsync(
            _protector.Unprotect(entity.ProtectedApiKey), entity.Model, cancellationToken);
        entity.IsConnectionVerified = isSuccessful;
        entity.LastConnectionTestAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("connection_tested", entity.Provider, entity.Model, false, isSuccessful);
        await SaveChangesAsync(cancellationToken);

        return new AiProviderConnectionTestDto
        {
            IsSuccessful = isSuccessful,
            Message = message,
            TestedAtUtc = entity.LastConnectionTestAtUtc
        };
    }

    public async Task<AiProviderConfigurationDto> ApproveLiveDataAsync(AiLiveDataApprovalRequest request, CancellationToken cancellationToken)
    {
        if (!request.AcknowledgeTerms || !string.Equals(request.TermsVersion, AiPrivacyPolicy.CurrentTermsVersion, StringComparison.Ordinal))
            throw new ArgumentException("Confirma las condiciones vigentes de privacidad, retención y límite de gasto.");
        var entity = await dbContext.AiProviderConfigurations.SingleOrDefaultAsync(cancellationToken)
            ?? throw new AiProviderUnavailableException("Configura primero el proveedor de IA.");
        if (!entity.IsConnectionVerified)
            throw new ArgumentException("Verifica la conexión con el proveedor antes de habilitar datos reales.");

        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException("No se pudo identificar al administrador.");
        entity.LiveDataApprovalTermsVersion = AiPrivacyPolicy.CurrentTermsVersion;
        entity.LiveDataApprovedByUserId = actorUserId;
        entity.LiveDataApprovedAtUtc = DateTime.UtcNow;
        entity.LiveDataApprovedProvider = entity.Provider;
        entity.LiveDataApprovedModel = entity.Model;
        entity.Version = Guid.NewGuid();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("live_data_approved", entity.Provider, entity.Model, false, true, AiPrivacyPolicy.CurrentTermsVersion);
        await SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<AiProviderConfigurationDto> RevokeLiveDataApprovalAsync(CancellationToken cancellationToken)
    {
        var entity = await dbContext.AiProviderConfigurations.SingleOrDefaultAsync(cancellationToken);
        if (entity is null || !IsLiveDataApproved(entity)) return Map(entity);

        var approvedTermsVersion = entity.LiveDataApprovalTermsVersion;
        ClearLiveDataApproval(entity);
        entity.Version = Guid.NewGuid();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit("live_data_revoked", entity.Provider, entity.Model, false, false, approvedTermsVersion);
        await SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid expectedVersion, CancellationToken cancellationToken)
    {
        var entity = await dbContext.AiProviderConfigurations.SingleOrDefaultAsync(cancellationToken);
        if (entity is null) return;
        if (entity.Version != expectedVersion)
            throw new ArgumentException("La configuración cambió. Actualiza la página e intenta nuevamente.");

        AddAudit("removed", entity.Provider, entity.Model, false, null);
        dbContext.AiProviderConfigurations.Remove(entity);
        await SaveChangesAsync(cancellationToken);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ArgumentException("La configuración cambió. Actualiza la página e intenta nuevamente.", exception);
        }
    }

    private void AddAudit(string action, string provider, string? model, bool credentialReplaced, bool? connectionSucceeded, string? termsVersion = null)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException("No se pudo identificar al administrador.");
        dbContext.AiProviderConfigurationAuditEvents.Add(new AiProviderConfigurationAuditEvent
        {
            ActorUserId = actorUserId,
            Action = action,
            Provider = provider,
            Model = model,
            TermsVersion = termsVersion,
            CredentialReplaced = credentialReplaced,
            ConnectionSucceeded = connectionSucceeded,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static void ValidateRequest(AiProviderConfigurationUpdateRequest request)
    {
        if (!AiProviders.IsSupported(request.Provider))
            throw new ArgumentException("El proveedor seleccionado no está disponible.");
        if (!ModelPattern.IsMatch(request.Model ?? string.Empty))
            throw new ArgumentException("Indica un identificador de modelo válido.");
        if (request.ApiKey is { Length: > 512 })
            throw new ArgumentException("La clave API supera el largo permitido.");
        if (request.ApiKey is not null && request.ApiKey.Any(char.IsControl))
            throw new ArgumentException("La clave API contiene caracteres no válidos.");
    }

    private static bool IsLiveDataApproved(AiProviderConfiguration entity) =>
        entity.LiveDataApprovalTermsVersion == AiPrivacyPolicy.CurrentTermsVersion
        && entity.LiveDataApprovedByUserId.HasValue
        && entity.LiveDataApprovedAtUtc.HasValue
        && entity.LiveDataApprovedProvider == entity.Provider
        && entity.LiveDataApprovedModel == entity.Model;

    private static void ClearLiveDataApproval(AiProviderConfiguration entity)
    {
        entity.LiveDataApprovalTermsVersion = null;
        entity.LiveDataApprovedByUserId = null;
        entity.LiveDataApprovedAtUtc = null;
        entity.LiveDataApprovedProvider = null;
        entity.LiveDataApprovedModel = null;
    }

    private static AiProviderConfigurationDto Map(AiProviderConfiguration? entity) => new()
    {
        IsConfigured = entity is not null && !string.IsNullOrWhiteSpace(entity.ProtectedApiKey),
        Provider = entity?.Provider ?? "OpenAI",
        Model = entity?.Model,
        ApiKeyHint = entity is null ? null : $"••••{entity.ApiKeyLastFour}",
        IsConnectionVerified = entity?.IsConnectionVerified ?? false,
        LastConnectionTestAtUtc = entity?.LastConnectionTestAtUtc,
        LiveDataApproved = entity is not null && IsLiveDataApproved(entity),
        LiveDataApprovedByUserId = entity?.LiveDataApprovedByUserId,
        LiveDataApprovedAtUtc = entity?.LiveDataApprovedAtUtc,
        LiveDataApprovalTermsVersion = entity?.LiveDataApprovalTermsVersion,
        Version = entity?.Version ?? Guid.Empty
    };
}
