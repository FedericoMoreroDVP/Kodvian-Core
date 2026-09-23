using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class S3FileStorageService : IFileStorageService, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucket;

    public S3FileStorageService(IOptions<StorageOptions> options)
    {
        var storage = options.Value;
        _bucket = storage.Bucket ?? throw new InvalidOperationException("Storage bucket is not configured.");

        var credentials = new BasicAWSCredentials(
            storage.AccessKey ?? throw new InvalidOperationException("Storage access key is not configured."),
            storage.SecretKey ?? throw new InvalidOperationException("Storage secret key is not configured."));

        var config = new AmazonS3Config
        {
            ForcePathStyle = storage.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(storage.ServiceUrl))
        {
            config.ServiceURL = storage.ServiceUrl;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(storage.Region) ? "auto" : storage.Region;
        }
        else if (!string.IsNullOrWhiteSpace(storage.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(storage.Region);
        }
        else
        {
            throw new InvalidOperationException("Storage ServiceUrl or Region must be configured for S3.");
        }

        _s3Client = new AmazonS3Client(credentials, config);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
        {
            throw new ArgumentException("El archivo no contiene información");
        }

        var sanitizedExtension = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.Trim();
        if (!sanitizedExtension.StartsWith(".", StringComparison.Ordinal))
        {
            sanitizedExtension = $".{sanitizedExtension}";
        }

        var key = $"{DateTime.UtcNow:yyyy}/{DateTime.UtcNow:MM}/{Guid.NewGuid():N}{sanitizedExtension}";

        using var stream = new MemoryStream(content);
        var request = CreateUploadRequest(_bucket, key, stream);

        await _s3Client.PutObjectAsync(request, cancellationToken);
        return key;
    }

    public async Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(storagePath);

        try
        {
            using var response = await _s3Client.GetObjectAsync(_bucket, key, cancellationToken);
            using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("El archivo solicitado no existe", storagePath, ex);
        }
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(storagePath);

        try
        {
            await _s3Client.DeleteObjectAsync(_bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Idempotent delete: missing object is not an error.
        }
    }

    public void Dispose()
    {
        _s3Client.Dispose();
    }

    internal static PutObjectRequest CreateUploadRequest(string bucket, string key, Stream content) => new()
    {
        BucketName = bucket,
        Key = key,
        InputStream = content,
        AutoCloseStream = true,
        // Some S3-compatible providers do not implement AWS's streaming payload signature.
        UseChunkEncoding = false
    };

    private static string NormalizeKey(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            throw new ArgumentException("La ruta del archivo es inválida", nameof(storagePath));
        }

        var key = storagePath.Replace('\\', '/').TrimStart('/');
        if (key.Contains("..", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La ruta del archivo es inválida", nameof(storagePath));
        }

        return key;
    }
}
