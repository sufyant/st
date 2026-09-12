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
        var user = await TenantUsers.FindAsync(tenantDbContext, request.ExternalUserId, cancellationToken);

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

        user.AssignRoles([]);
        user.Disable();

        return Result.Success();
    }
}
