using System.Collections.Concurrent;
using ControlPlane.Application.Ports;

namespace ControlPlane.IntegrationTests;

// Clerk, as far as the module uses it. Tests use email addresses and user ids of their own, so they share one instance.
public sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly ConcurrentDictionary<string, string[]> _verifiedEmails = new();

    public ConcurrentBag<(string Email, Guid InvitationId, Uri AcceptLink)> Invitations { get; } = [];

    public ConcurrentBag<string> Revoked { get; } = [];

    public void AddAccount(string externalUserId, params string[] verifiedEmails) => _verifiedEmails[externalUserId] = verifiedEmails;

    public Task<bool> HasAccountAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(_verifiedEmails.Values.Any(emails => emails.Contains(email, StringComparer.OrdinalIgnoreCase)));

    public Task<IdentityProviderInvitation> InviteAsync(string email, Guid invitationId, Uri acceptLink, CancellationToken cancellationToken)
    {
        Invitations.Add((email, invitationId, acceptLink));
        return Task.FromResult(new IdentityProviderInvitation(ProviderInvitationIdOf(invitationId), new Uri($"https://clerk.test/invitations/{invitationId}")));
    }

    public Task RevokeInvitationAsync(string invitationId, CancellationToken cancellationToken)
    {
        Revoked.Add(invitationId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_verifiedEmails.GetValueOrDefault(externalUserId, []));

    // The id Clerk gives the invitation it creates for ours.
    public static string ProviderInvitationIdOf(Guid invitationId) => $"inv_{invitationId:N}";
}
