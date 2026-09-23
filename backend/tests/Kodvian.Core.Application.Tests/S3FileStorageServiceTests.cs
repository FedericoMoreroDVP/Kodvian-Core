using Kodvian.Core.Infrastructure.Services;

namespace Kodvian.Core.Application.Tests;

public class S3FileStorageServiceTests
{
    [Fact]
    public void UploadRequestDisablesChunkEncodingForS3CompatibleStorage()
    {
        using var content = new MemoryStream([1, 2, 3]);

        var request = S3FileStorageService.CreateUploadRequest("evidencias", "2026/09/prueba.png", content);

        Assert.False(request.UseChunkEncoding);
        Assert.Equal("evidencias", request.BucketName);
        Assert.Equal("2026/09/prueba.png", request.Key);
        Assert.Same(content, request.InputStream);
    }
}
