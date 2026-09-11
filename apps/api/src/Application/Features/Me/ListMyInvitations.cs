using Application.Abstractions;
using Application.Results;
using Domain.ControlPlane.Invitations;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Me;

public sealed record MyInvitation(
    Guid Id,
    string TenantAlias,
    string Email,
    string RoleCode,
    DateTimeOffset ExpiresAt);

public sealed record ListMyInvitationsQuery : IQuery<IReadOnlyList<MyInvitation>>;

public sealed class ListMyInvitationsHandler(
    ControlPlaneDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<ListMyInvitationsQuery, IReadOnlyList<MyInvitation>>
{
    public async Task<Result<IReadOnlyList<MyInvitation>>> HandleAsync(
        ListMyInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetEmail(out var email))
        {
            return Result<IReadOnlyList<MyInvitation>>.Failure(MeErrors.EmailClaimRequired);
        }

        var now = timeProvider.GetUtcNow();
        var rows = await dbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.Email == email
                                 && invitation.Status == InvitationStatus.Pending
                                 && invitation.ExpiresAt >= now)
            .Join(
                dbContext.Tenants,
                invitation => invitation.TenantId,
                tenant => tenant.Id,
                (invitation, tenant) => new { Invitation = invitation, Tenant = tenant })
            .ToListAsync(cancellationToken);
        var invitations = rows
            .Select(row => new MyInvitation(
                row.Invitation.Id,
                row.Tenant.Alias.Value,
                row.Invitation.Email.Value,
                row.Invitation.RoleCode,
                row.Invitation.ExpiresAt))
            .ToList();

        return Result<IReadOnlyList<MyInvitation>>.Success(invitations);
    }
}
