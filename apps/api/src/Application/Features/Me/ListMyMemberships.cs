using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Me;

public sealed record MyMembership(Guid TenantId, string TenantAlias, string TenantStatus);

public sealed record ListMyMembershipsQuery : IQuery<IReadOnlyList<MyMembership>>;

public sealed class ListMyMembershipsHandler(
    ControlPlaneDbContext dbContext,
    ICurrentUser currentUser) : IRequestHandler<ListMyMembershipsQuery, IReadOnlyList<MyMembership>>
{
    public async Task<Result<IReadOnlyList<MyMembership>>> HandleAsync(
        ListMyMembershipsQuery request,
        CancellationToken cancellationToken)
    {
        var externalUserId = currentUser.Id;
        // The projection runs after materialisation: EF cannot translate value object access
        // inside a join result selector.
        var tenants = await dbContext.Memberships
            .AsNoTracking()
            .Where(membership => membership.ExternalUserId == externalUserId)
            .Join(
                dbContext.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (_, tenant) => tenant)
            .OrderBy(tenant => tenant.Alias)
            .ToListAsync(cancellationToken);
        var memberships = tenants
            .Select(tenant => new MyMembership(tenant.Id, tenant.Alias.Value, tenant.Status.ToString()))
            .ToList();

        return Result<IReadOnlyList<MyMembership>>.Success(memberships);
    }
}
