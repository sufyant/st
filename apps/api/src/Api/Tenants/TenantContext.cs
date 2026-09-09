namespace Api.Tenants;

public sealed class TenantContext
{
    private Guid? tenantId;
    private string? alias;

    public Guid TenantId => tenantId ?? throw new InvalidOperationException("Tenant context has not been resolved.");

    public string Alias => alias ?? throw new InvalidOperationException("Tenant context has not been resolved.");

    internal void Set(Guid resolvedTenantId, string resolvedAlias)
    {
        tenantId = resolvedTenantId;
        alias = resolvedAlias;
    }
}
