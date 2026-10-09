using ControlPlane.Application.Ports;
using SharedKernel;

namespace ControlPlane.Application.Tenants;

/// <summary>The signed-in user's active tenants, a page at a time. It runs without a tenant.</summary>
public sealed record ListMyTenants(string UserId, PageRequest Paging);

/// <summary>A tenant as its members see it.</summary>
public sealed record TenantSummary(Guid Id, string Name, string Slug);

public static class ListMyTenantsHandler
{
    public static async Task<Result<ListPage<TenantSummary>>> HandleAsync(
        ListMyTenants query,
        IUserTenants tenants,
        CancellationToken cancellationToken) =>
        (await tenants.ListActiveAsync(query.UserId, query.Paging, cancellationToken))
            .Map(tenant => new TenantSummary(tenant.Id, tenant.Name, tenant.Slug));
}
