using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;

namespace ControlPlane.Application.Invitations;

// Issuing an invitation, whoever asks for it: the record is saved in the command's transaction, and its delivery is a message the
// outbox sends once that transaction commits (0029). Nothing here calls out of the process.
internal static class InvitationIssuer
{
    public static async Task<(InvitationDetails Invitation, DeliverInvitation Delivery)> IssueAsync(
        string email,
        Role role,
        Guid invitedBy,
        ITenantCatalog catalog,
        InvitationSettings settings,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var invitation = Invitation.Create(Guid.CreateVersion7(now), catalog.TenantId, email, role, invitedBy, now, settings.Lifetime);

        catalog.Add(invitation);
        await catalog.SaveChangesAsync(cancellationToken);

        return (
            new InvitationDetails(invitation.Id, invitation.Email, invitation.RoleId, invitation.Status.ToString(), invitation.ExpiresAt),
            new DeliverInvitation(invitation.Id));
    }
}
