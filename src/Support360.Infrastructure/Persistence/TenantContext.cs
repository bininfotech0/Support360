using Support360.Application.Abstractions;

namespace Support360.Infrastructure.Persistence;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public bool IsSystem { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        }

        if (IsSystem)
        {
            return;
        }

        if (TenantId is not null && TenantId != tenantId)
        {
            throw new InvalidOperationException("Tenant context cannot be changed within a request scope.");
        }

        TenantId = tenantId;
    }

    public void SetSystem()
    {
        IsSystem = true;
        TenantId = null;
    }
}
