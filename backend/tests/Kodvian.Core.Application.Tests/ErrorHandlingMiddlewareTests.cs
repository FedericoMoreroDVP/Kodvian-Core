using Kodvian.Core.Api.Middleware;
using Kodvian.Core.Application.Common.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kodvian.Core.Application.Tests;

public class ErrorHandlingMiddlewareTests
{
    [Fact]
    public async Task StorageFailureReturnsSafeServiceUnavailableResponse()
    {
        var middleware = new ErrorHandlingMiddleware(_ => throw new StorageUnavailableException(), NullLogger<ErrorHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Contains("No se pudo guardar la evidencia", response);
        Assert.DoesNotContain("bucket", response, StringComparison.OrdinalIgnoreCase);
    }
}
