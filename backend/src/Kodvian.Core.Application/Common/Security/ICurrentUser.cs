namespace Kodvian.Core.Application.Common.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? SessionVersion { get; }
}
