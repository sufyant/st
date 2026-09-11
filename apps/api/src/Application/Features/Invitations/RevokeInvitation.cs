using Application.Abstractions;
using Application.Results;
using Domain.ControlPlane.Invitations;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Invitations;

[RequiresPermission(TenantPermissions.InvitationsManage)]
public sealed record RevokeInvitationCommand(Guid Id) : ICommand<Unit>;

public sealed class RevokeInvitationHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantContext tenantContext,
    TimeProvider timeProvider) : IRequestHandler<RevokeInvitationCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RevokeInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var invitation = await controlPlaneDbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.Id == request.Id
                         && candidate.TenantId == tenantContext.TenantId
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Result<Unit>.Failure(Error.NotFound(
                "invitation.missing",
                "The invitation does not exist or is no longer pending."));
        }

        invitation.Revoke(timeProvider.GetUtcNow());

        return Result.Success();
    }
}
