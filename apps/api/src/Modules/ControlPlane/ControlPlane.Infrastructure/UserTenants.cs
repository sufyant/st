using ControlPlane.Application.Ports;
using ControlPlane.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace ControlPlane.Infrastructure;

// The catalog user is found by the identity provider's id in catalog.users, which belongs to no tenant. Their memberships are
// then read in a transaction that declares that user (R11), so the own_memberships policy decides what is seen; the query adds
// no filter of its own.
internal sealed class UserTenants(CatalogDbContext catalog) : IUserTenants
{
    async Task<ListPage<Tenant>> IUserTenants.ListActiveAsync(string externalUserId, PageRequest paging, CancellationToken cancellationToken)
    {
        var userId = await catalog.Users
            .Where(user => user.ExternalId == externalUserId)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (userId is not { } id)
        {
            return new ListPage<Tenant>([], paging, 0);
        }

        await using var transaction = await catalog.Database.BeginTransactionAsync(cancellationToken);
        await catalog.DeclareUserAsync(id, cancellationToken);
        var tenants =
            from membership in catalog.Memberships
            join tenant in catalog.Tenants on membership.TenantId equals tenant.Id
            where tenant.Status == TenantStatus.Active
            select tenant;

        var total = await tenants.CountAsync(cancellationToken);
        var page = await tenants
            .OrderBy(tenant => tenant.Name)
            .ThenBy(tenant => tenant.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ListPage<Tenant>(page, paging, total);
    }
}
