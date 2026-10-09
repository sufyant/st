using System.Collections.Concurrent;
using ControlPlane.Application.Ports;
using Notifications.Application;
using Notifications.Application.Ports;

namespace Onboarding.EndToEndTests;

// Clerk, as far as the application uses it. Someone without an account signs up through the provider's invitation, which carries
// our accept link.
internal sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly ConcurrentDictionary<string, string[]> _verifiedEmails = new();

    public ConcurrentBag<(string Email, Guid InvitationId, Uri AcceptLink)> Invitations { get; } = [];

    public void SignUp(string externalUserId, string verifiedEmail) => _verifiedEmails[externalUserId] = [verifiedEmail];

    public Task<bool> HasAccountAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(_verifiedEmails.Values.Any(emails => emails.Contains(email, StringComparer.OrdinalIgnoreCase)));

    public Task<IdentityProviderInvitation> InviteAsync(string email, Guid invitationId, Uri acceptLink, CancellationToken cancellationToken)
    {
        Invitations.Add((email, invitationId, acceptLink));
        return Task.FromResult(new IdentityProviderInvitation($"inv_{invitationId:N}", new Uri($"https://clerk.test/invitations/{invitationId}")));
    }

    public Task RevokeInvitationAsync(string invitationId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_verifiedEmails.GetValueOrDefault(externalUserId, []));
}

// The email service, as the application sends through it.
internal sealed class FakeEmailChannel : IEmailChannel
{
    public ConcurrentBag<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage email, CancellationToken cancellationToken)
    {
        Sent.Add(email);
        return Task.CompletedTask;
    }
}
