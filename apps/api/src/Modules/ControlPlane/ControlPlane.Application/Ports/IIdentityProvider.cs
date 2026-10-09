namespace ControlPlane.Application.Ports;

/// <summary>The identity provider (Clerk): it authenticates people; who they are in a tenant stays in our catalog.</summary>
public interface IIdentityProvider
{
    Task<bool> HasAccountAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Lets someone without an account sign up while sign-up is invite-only, without the provider sending an email of its
    /// own. Returns the provider's invitation: its id, and the link that signs them up and then lands them on
    /// <paramref name="acceptLink"/>.
    /// </summary>
    Task<IdentityProviderInvitation> InviteAsync(string email, Guid invitationId, Uri acceptLink, CancellationToken cancellationToken);

    /// <summary>Withdraws an invitation <see cref="InviteAsync"/> created. One that is no longer pending is left as it is.</summary>
    Task RevokeInvitationAsync(string invitationId, CancellationToken cancellationToken);

    /// <summary>The email addresses the user has verified with the provider.</summary>
    Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken);
}

/// <summary>An invitation the identity provider holds for someone without an account.</summary>
public sealed record IdentityProviderInvitation(string Id, Uri Link);
