using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

// The one catalog writer that is not bound to a tenant (0021): the system job that closes expired invitations works across all
// tenants in one statement. Acceptance locks the invitation's row too, so the two never both change it.
internal sealed class InvitationExpiry(CatalogDbContext catalog) : IInvitationExpiry
{
    public Task CloseExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        catalog.Invitations
            .Where(invitation => invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt <= now)
            .ExecuteUpdateAsync(invitations => invitations.SetProperty(invitation => invitation.Status, InvitationStatus.Expired), cancellationToken);
}
