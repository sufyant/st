using ControlPlane.Domain.Tenants;
using Tenancy;

namespace ControlPlane.Infrastructure;

/// <summary>
/// The one way to reach catalog rows that belong to a tenant. The catalog has no row level security, so this access point,
/// bound to the active tenant, is what keeps one tenant's rows from another (0021).
/// </summary>
internal sealed class TenantCatalog(CatalogDbContext catalog, TenantContext tenant)
{
    public IQueryable<Membership> Memberships
    {
        get
        {
            var tenantId = ActiveTenant;
            return catalog.Memberships.Where(membership => membership.TenantId == tenantId);
        }
    }

    private Guid ActiveTenant =>
        tenant.TenantId ?? throw new InvalidOperationException("Tenant-owned catalog rows are reached only inside a tenant.");

    public void AddMember(Guid userId) => catalog.Memberships.Add(new Membership(ActiveTenant, userId));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => catalog.SaveChangesAsync(cancellationToken);
}
