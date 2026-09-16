namespace Kodvian.Core.Application.Auth.Dtos;

public class TokenGenerationDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
    public Guid SessionVersion { get; set; }
    public Guid? DeveloperId { get; set; }
    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();
}
