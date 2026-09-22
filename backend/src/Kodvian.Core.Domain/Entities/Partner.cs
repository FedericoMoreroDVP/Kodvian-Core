namespace Kodvian.Core.Domain.Entities;

public class Partner : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid? DeveloperId { get; set; }
    public Developer? Developer { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
}
