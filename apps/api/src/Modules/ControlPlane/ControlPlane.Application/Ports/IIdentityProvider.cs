namespace ControlPlane.Application.Ports;

/// <summary>The identity provider (Clerk): it authenticates people; who they are in a tenant stays in our catalog.</summary>
public interface IIdentityProvider
{
    Task<bool> HasAccountAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Lets someone without an account sign up while sign-up is invite-only, without the provider sending an email of its
    /// own. Returns the link that signs them up and then lands them on <paramref name="acceptLink"/>.
    /// </summary>
    Task<Uri> InviteAsync(string email, Guid invitationId, Uri acceptLink, CancellationToken cancellationToken);

    /// <summary>The email addresses the user has verified with the provider.</summary>
    Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken);
}
