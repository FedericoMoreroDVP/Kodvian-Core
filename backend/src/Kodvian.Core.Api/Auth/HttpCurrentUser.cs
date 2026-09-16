using System.Security.Claims;
using Kodvian.Core.Application.Common.Security;

namespace Kodvian.Core.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => Parse(ClaimTypes.NameIdentifier);
    public Guid? SessionVersion => Parse(CustomClaimTypes.SessionVersion);
    private Guid? Parse(string claim) => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(claim), out var value) ? value : null;
}
