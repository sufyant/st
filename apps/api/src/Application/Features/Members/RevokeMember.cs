using Application.Abstractions;
using Application.Results;
using Domain.Access;
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
            return Result<Unit>.Failure(MemberErrors.Missing);
        }

        var roster = await TenantUsers.LoadOwnerRosterAsync(tenantDbContext, cancellationToken);

        if (roster.IsLastOwner(user.Id))
        {
            return Result<Unit>.Failure(MemberErrors.LastOwner);
        }

        var externalUserId = ExternalUserId.Create(request.ExternalUserId);
        var memberships = await controlPlaneDbContext.Memberships
            .Where(membership => membership.TenantId == tenantContext.TenantId
                                 && membership.ExternalUserId == externalUserId)
            .ToListAsync(cancellationToken);
        controlPlaneDbContext.Memberships.RemoveRange(memberships);

        var assignments = await tenantDbContext.UserRoles
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync(cancellationToken);
        tenantDbContext.UserRoles.RemoveRange(assignments);
        user.Disable();

        return Result.Success();
    }
}
