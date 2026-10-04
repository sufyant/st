using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Users;
using SharedKernel;

namespace ControlPlane.Application.Invitations;

/// <summary>
/// Accepts an invitation in its tenant: the catalog user, when new, and the membership are created in the same transaction as
/// the invitation is used up (0029).
/// </summary>
public sealed record AcceptInvitation(string Token, string UserId);

public sealed record InvitationAccepted(string TenantSlug);

public static class AcceptInvitationHandler
{
    public static async Task<Result<InvitationAccepted>> HandleAsync(
        AcceptInvitation command,
        ITenantCatalog catalog,
        IIdentityProvider identity,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await catalog.FindInvitationForUpdateAsync(InvitationToken.Hash(command.Token), cancellationToken) is not { } invitation)
        {
            return Errors.InvitationNotFound;
        }

        var now = time.GetUtcNow();
        var existing = await catalog.FindUserAsync(command.UserId, cancellationToken);
        if (existing is not null && await catalog.FindMembershipAsync(existing.Id, cancellationToken) is not null)
        {
            return Error.Conflict("membership.exists", "You are already a member of this tenant.");
        }

        var user = existing ?? new User(Guid.CreateVersion7(now), command.UserId);
        var accepted = invitation.Accept(await identity.FindVerifiedEmailsAsync(command.UserId, cancellationToken), user.Id, now);
        if (!accepted.IsSuccess)
        {
            return accepted.Error;
        }

        if (existing is null)
        {
            catalog.Add(user);
        }

        catalog.AddMember(user.Id, invitation.RoleId);
        await catalog.SaveChangesAsync(cancellationToken);
        return new InvitationAccepted(await catalog.FindSlugAsync(cancellationToken));
    }
}
