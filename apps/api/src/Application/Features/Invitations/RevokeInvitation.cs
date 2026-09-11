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
    TenantContext tenantContext) : IRequestHandler<RevokeInvitationCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RevokeInvitationCommand request,
        CancellationToken cancellationToken)
    {
        // A lookup key skips the validating factory: an unmatched id should miss the query
        // below and fall through to the not-found result, not throw.
        var invitationId = new InvitationId(request.Id);
        var invitation = await controlPlaneDbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.Id == invitationId
                         && candidate.TenantId == tenantContext.TenantId
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Result<Unit>.Failure(Error.NotFound(
                "invitation.missing",
                "The invitation does not exist or is no longer pending."));
        }

        invitation.Revoke();

        return Result.Success();
    }
}
