namespace ControlPlane.Application.Ports;

/// <summary>The identity provider (Clerk): it authenticates people; who they are in a tenant stays in our catalog.</summary>
public interface IIdentityProvider
{
    /// <summary>The email addresses the user has verified with the provider.</summary>
    Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken);
}
