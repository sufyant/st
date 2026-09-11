using Domain.ControlPlane.Tenants;

namespace Application.Abstractions;

public sealed class TenantContext
{
    private TenantId? tenantId;
    private string? alias;
    private string? databaseName;

    public bool IsResolved => tenantId is not null;

    public TenantId TenantId => tenantId ?? throw NotResolved();

    public string Alias => alias ?? throw NotResolved();

    public string DatabaseName => databaseName ?? throw NotResolved();

    public void Set(TenantId resolvedTenantId, string resolvedAlias, string resolvedDatabaseName)
    {
        tenantId = resolvedTenantId;
        alias = resolvedAlias;
        databaseName = resolvedDatabaseName;
    }

    private static InvalidOperationException NotResolved() =>
        new("Tenant context has not been resolved.");
}
