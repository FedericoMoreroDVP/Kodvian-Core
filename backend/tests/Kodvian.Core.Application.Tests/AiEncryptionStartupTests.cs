using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kodvian.Core.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kodvian.Core.Application.Tests;

public sealed class AiEncryptionStartupTests
{
    [Fact]
    public void ProductionWithoutAiEncryptionConfigurationFailsWithCertificateVariableName()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructureInProduction());

        Assert.Equal("AiEncryption:CertificateBase64 is required outside Development.", exception.Message);
    }

    [Fact]
    public void ProductionWithEmptyCertificatePasswordFailsWithPasswordVariableName()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddInfrastructureInProduction(("AiEncryption:CertificateBase64", CreatePfxBase64("test-password")),
                ("AiEncryption:CertificatePassword", "")));

        Assert.Equal("AiEncryption:CertificatePassword is required when AiEncryption:CertificateBase64 is configured.", exception.Message);
    }

    [Fact]
    public void ProductionWithInvalidCertificateBase64FailsClearly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddInfrastructureInProduction(("AiEncryption:CertificateBase64", "not-base64"),
                ("AiEncryption:CertificatePassword", "test-password")));

        Assert.Equal("AiEncryption:CertificateBase64 is not valid Base64.", exception.Message);
    }

    [Fact]
    public void ProductionWithIncorrectCertificatePasswordFailsClearly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddInfrastructureInProduction(("AiEncryption:CertificateBase64", CreatePfxBase64("correct-password")),
                ("AiEncryption:CertificatePassword", "incorrect-password")));

        Assert.Equal("AiEncryption:CertificateBase64 could not be opened with AiEncryption:CertificatePassword.", exception.Message);
    }

    [Fact]
    public void ProductionWithCertificateWithoutPrivateKeyFailsClearly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddInfrastructureInProduction(("AiEncryption:CertificateBase64", CreatePublicCertificateBase64()),
                ("AiEncryption:CertificatePassword", "test-password")));

        Assert.Equal("AiEncryption:CertificateBase64 must contain a PFX certificate with a private key.", exception.Message);
    }

    [Fact]
    public void ProductionWithValidAiEncryptionConfigurationStartsServiceRegistration()
    {
        var exception = Record.Exception(() =>
            AddInfrastructureInProduction(("AiEncryption:CertificateBase64", CreatePfxBase64("test-password")),
                ("AiEncryption:CertificatePassword", "test-password")));

        Assert.Null(exception);
    }

    private static void AddInfrastructureInProduction(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
        new ServiceCollection().AddInfrastructure(configuration, isDevelopment: false);
    }

    private static string CreatePfxBase64(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Kodvian Core Tests", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, password));
    }

    private static string CreatePublicCertificateBase64()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Kodvian Core Tests", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    }
}
