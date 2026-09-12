using Application.Abstractions;
using Application.Results;
using Domain.Shared;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

[RequiresPermission(TenantPermissions.MembersManage)]
public sealed record RevokeMemberCommand(string ExternalUserId) : ICommand<Unit>;

public sealed class RevokeMemberHandler(
    TenantDbContext tenantDbContext,
    ControlPlaneDbContext controlPlaneDbContext,
    TenantContext tenantContext) : IRequestHandler<RevokeMemberCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RevokeMemberCommand request,
        CancellationToken cancellationToken)
    {
        var user = await MemberQueries.FindAsync(tenantDbContext, request.ExternalUserId, cancellationToken);

        if (user is null)
        {
            return MemberErrors.Missing;
        }

        var externalUserId = ExternalUserId.Create(request.ExternalUserId);
        var memberships = await controlPlaneDbContext.Memberships
            .Where(membership => membership.TenantId == tenantContext.TenantId
                                 && membership.ExternalUserId == externalUserId)
            .ToListAsync(cancellationToken);
        controlPlaneDbContext.Memberships.RemoveRange(memberships);

        // The role stays on the row: revoking access does not erase who this person was while
        // they had it, and a future re-invite always assigns a fresh role before it matters again.
        user.Disable();

        return Result.Success();
    }
}
