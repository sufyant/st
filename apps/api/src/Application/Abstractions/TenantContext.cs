using Domain.ControlPlane.Tenants;

namespace Application.Abstractions;

public sealed class TenantContext
{
    private TenantId? tenantId;
    private string? alias;
    private string? databaseName;
    private string? username;
    private string? password;

    public bool IsResolved => tenantId is not null;

    public TenantId TenantId => tenantId ?? throw NotResolved();

    public string Alias => alias ?? throw NotResolved();

    public string DatabaseName => databaseName ?? throw NotResolved();

    public string Username => username ?? throw NotResolved();

    public string Password => password ?? throw NotResolved();

    public void Set(
        TenantId resolvedTenantId,
        string resolvedAlias,
        string resolvedDatabaseName,
        string resolvedUsername,
        string resolvedPassword)
    {
        tenantId = resolvedTenantId;
        alias = resolvedAlias;
        databaseName = resolvedDatabaseName;
        username = resolvedUsername;
        password = resolvedPassword;
    }

    private static InvalidOperationException NotResolved() =>
        new("Tenant context has not been resolved.");
}
