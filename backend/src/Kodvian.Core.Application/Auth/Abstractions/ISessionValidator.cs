namespace Kodvian.Core.Application.Auth.Abstractions;

public interface ISessionValidator
{
    Task<bool> IsValidAsync(Guid userId, Guid sessionVersion, CancellationToken ct);
}
