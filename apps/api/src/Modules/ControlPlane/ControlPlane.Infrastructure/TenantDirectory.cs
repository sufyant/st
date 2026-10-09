using ControlPlane.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace ControlPlane.Infrastructure;

// Resolves a tenant route before any tenant is declared. The tenant comes from its slug in catalog.tenants, which belongs to no
// tenant; the membership is then read in a transaction that declares that tenant first (R4), so row level security decides what
// it sees. The membership comes with its role's permissions, so the read stays one query.
internal sealed class TenantDirectory(CatalogDbContext catalog) : ITenantDirectory
{
    public async Task<TenantMembership?> FindMembershipAsync(string slug, string externalUserId, CancellationToken cancellationToken)
    {
        var tenantId = await catalog.Tenants
            .Where(tenant => tenant.Slug == slug && tenant.Status == TenantStatus.Active)
            .Select(tenant => (Guid?)tenant.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (tenantId is not { } id)
        {
            return null;
        }

        await using var transaction = await catalog.Database.BeginTransactionAsync(cancellationToken);
        await catalog.DeclareTenantAsync(id, cancellationToken);
        var memberRole = await (
                from membership in catalog.Memberships
                join user in catalog.Users on membership.UserId equals user.Id
                join role in catalog.Roles on membership.RoleId equals role.Id
                where user.ExternalId == externalUserId
                select role)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return memberRole is null ? null : new TenantMembership(id, memberRole.Permissions);
    }
}
