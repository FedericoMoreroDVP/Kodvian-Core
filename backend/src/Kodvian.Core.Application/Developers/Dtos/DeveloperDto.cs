namespace Kodvian.Core.Application.Developers.Dtos;

public class DeveloperDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public bool HasSystemAccess { get; set; }
    public bool IsSystemAccessActive { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
}
