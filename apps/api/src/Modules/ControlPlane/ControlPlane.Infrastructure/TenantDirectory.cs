using ControlPlane.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace ControlPlane.Infrastructure;

// Reads memberships before any tenant is known, so it is one of the catalog readers that are not bound to a tenant (0015, 0021).
// The membership comes with its role's permissions, so resolving a request stays one query.
internal sealed class TenantDirectory(CatalogDbContext catalog) : ITenantDirectory
{
    public async Task<TenantMembership?> FindMembershipAsync(string slug, string externalUserId, CancellationToken cancellationToken)
    {
        var found = await (
                from membership in catalog.Memberships
                join tenant in catalog.Tenants on membership.TenantId equals tenant.Id
                join user in catalog.Users on membership.UserId equals user.Id
                join role in catalog.Roles on membership.RoleId equals role.Id
                where tenant.Slug == slug && tenant.Status == TenantStatus.Active && user.ExternalId == externalUserId
                select new { membership.TenantId, Role = role })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return found is null ? null : new TenantMembership(found.TenantId, found.Role.Permissions);
    }
}
