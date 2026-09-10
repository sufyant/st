using Application.Abstractions;
using Application.Results;
using Domain.Access;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Invitations;

public sealed record PendingInvitation(
    Guid Id,
    string Email,
    string RoleCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

[RequiresPermission(TenantPermissions.InvitationsManage)]
public sealed record ListPendingInvitationsQuery : IQuery<IReadOnlyList<PendingInvitation>>;

public sealed class ListPendingInvitationsHandler(
    ControlPlaneDbContext controlPlaneDbContext,
    TenantContext tenantContext) : IRequestHandler<ListPendingInvitationsQuery, IReadOnlyList<PendingInvitation>>
{
    public async Task<Result<IReadOnlyList<PendingInvitation>>> HandleAsync(
        ListPendingInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var invitations = await controlPlaneDbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.TenantId == tenantContext.TenantId
                                 && invitation.Status == InvitationStatus.Pending)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Select(invitation => new PendingInvitation(
                invitation.Id,
                invitation.Email.Value,
                invitation.RoleCode,
                invitation.CreatedAt,
                invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PendingInvitation>>.Success(invitations);
    }
}
