using System.Collections.Concurrent;
using ControlPlane.Application.Ports;

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

    public Task<Uri> InviteAsync(string email, Guid invitationId, Uri acceptLink, CancellationToken cancellationToken)
    {
        Invitations.Add((email, invitationId, acceptLink));
        return Task.FromResult(new Uri($"https://clerk.test/invitations/{invitationId}"));
    }

    public Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_verifiedEmails.GetValueOrDefault(externalUserId, []));
}

// The invitation email, as the application sends it.
internal sealed class FakeInvitationSender : IInvitationSender
{
    public ConcurrentBag<(string Email, Uri Link)> Sent { get; } = [];

    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken)
    {
        Sent.Add((email, link));
        return Task.CompletedTask;
    }
}
