namespace Kodvian.Core.Application.Administration.Dtos;

public class UserListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public Guid? DeveloperId { get; set; }
    public Guid SessionVersion { get; set; }
}
