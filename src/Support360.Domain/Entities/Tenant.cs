namespace Support360.Domain.Entities;

public sealed class Tenant : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public bool IsDemo { get; set; }
    public int RequiredChangeApprovals { get; set; } = 1;
    public bool RequireChangeSeparationOfDuties { get; set; } = true;
    public List<string> HttpCheckAllowedHosts { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
}
