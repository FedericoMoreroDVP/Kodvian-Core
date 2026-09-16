using System.Security.Claims;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Kodvian.Core.Api.Auth;

public static class SessionTokenValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal?.FindFirstValue(CustomClaimTypes.SessionVersion), out var version)
            || !await context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>()
                .IsValidAsync(userId, version, context.HttpContext.RequestAborted))
        {
            context.Fail("La sesión cambió o el usuario está inactivo. Inicia sesión nuevamente.");
        }
    }
}
