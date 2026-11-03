namespace Support360.Application.Abstractions;

public interface ITenantContext
{
    Guid? TenantId { get; }
    bool IsSystem { get; }
    void SetTenant(Guid tenantId);
    void SetSystem();
}
