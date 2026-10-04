using ControlPlane.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace ControlPlane.Infrastructure;

// Reads memberships before any tenant is known, so it is the one catalog reader that is not bound to a tenant (0015, 0021).
internal sealed class TenantDirectory(CatalogDbContext catalog) : ITenantDirectory
{
    public Task<Guid?> FindMemberTenantAsync(string slug, string externalUserId, CancellationToken cancellationToken) =>
        catalog.Memberships.AsNoTracking()
            .Where(membership => catalog.Tenants.Any(tenant =>
                tenant.Id == membership.TenantId && tenant.Slug == slug && tenant.Status == TenantStatus.Active))
            .Where(membership => catalog.Users.Any(user => user.Id == membership.UserId && user.ExternalId == externalUserId))
            .Select(membership => (Guid?)membership.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
}
