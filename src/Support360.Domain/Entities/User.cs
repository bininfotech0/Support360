namespace Support360.Domain.Entities;

public sealed class User : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Email { get; set; }
    public required string EmailNormalized { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}
