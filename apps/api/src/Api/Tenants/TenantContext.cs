using Domain.Tenants;

namespace Api.Tenants;

public sealed class TenantContext
{
    private Tenant? tenant;

    public Guid TenantId => tenant?.Id ?? throw new InvalidOperationException("Tenant context has not been resolved.");

    public string Alias => tenant?.Alias.Value ?? throw new InvalidOperationException("Tenant context has not been resolved.");

    internal void Set(Tenant resolvedTenant)
    {
        tenant ??= resolvedTenant;
    }
}
