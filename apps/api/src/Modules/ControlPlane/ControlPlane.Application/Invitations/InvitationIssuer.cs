using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;

namespace ControlPlane.Application.Invitations;

// Issuing an invitation, whoever asks for it: the record with the token's hash, a provider invitation for someone without an
// account, and the link sent to the invited person (0029).
internal static class InvitationIssuer
{
    public static async Task<InvitationDetails> IssueAsync(
        string email,
        Role role,
        Guid invitedBy,
        InvitationServices services,
        CancellationToken cancellationToken)
    {
        var now = services.Time.GetUtcNow();
        var token = InvitationToken.Generate();
        var invitation = Invitation.Create(
            Guid.CreateVersion7(now), services.Catalog.TenantId, email, role, invitedBy, token, now, services.Settings.Lifetime);

        var acceptLink = services.Settings.AcceptLink(token);
        var link = await services.Identity.HasAccountAsync(invitation.Email, cancellationToken)
            ? acceptLink
            : await services.Identity.InviteAsync(invitation.Email, invitation.Id, acceptLink, cancellationToken);

        services.Catalog.Add(invitation);
        await services.Catalog.SaveChangesAsync(cancellationToken);

        // Sent after the record is saved; if sending fails, the transaction is not committed and the invitation does not exist.
        await services.Sender.SendAsync(invitation.Email, link, cancellationToken);

        return new InvitationDetails(invitation.Id, invitation.Email, invitation.RoleId, invitation.Status.ToString(), invitation.ExpiresAt);
    }
}

internal sealed record InvitationServices(
    ITenantCatalog Catalog,
    IIdentityProvider Identity,
    IInvitationSender Sender,
    InvitationSettings Settings,
    TimeProvider Time);
