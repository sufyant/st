using System.Collections.Concurrent;
using ControlPlane.Application.Ports;

namespace ControlPlane.IntegrationTests;

// Clerk, as far as the module uses it. Tests use email addresses and user ids of their own, so they share one instance.
public sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly ConcurrentDictionary<string, string[]> _verifiedEmails = new();

    public ConcurrentBag<(string Email, Guid InvitationId, Uri AcceptLink)> Invitations { get; } = [];

    public void AddAccount(string externalUserId, params string[] verifiedEmails) => _verifiedEmails[externalUserId] = verifiedEmails;

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

public sealed class FakeInvitationSender : IInvitationSender
{
    public ConcurrentBag<(string Email, Uri Link)> Sent { get; } = [];

    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken)
    {
        Sent.Add((email, link));
        return Task.CompletedTask;
    }

    public Uri LinkSentTo(string email) => Sent.Single(sent => sent.Email == email).Link;
}

// An email channel that is down.
public sealed class FailingInvitationSender : IInvitationSender
{
    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The email channel is down.");
}
