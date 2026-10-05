using System.Collections.Concurrent;
using ControlPlane.Application.Ports;

namespace Api.IntegrationTests;

// Clerk, as far as the application uses it.
internal sealed class FakeIdentityProvider : IIdentityProvider
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

    // Lets a test look at the database at the moment the application asks for a user's verified email addresses.
    public Func<Task>? WhileReadingVerifiedEmails { get; set; }

    public async Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken)
    {
        if (WhileReadingVerifiedEmails is { } probe)
        {
            await probe();
        }

        return _verifiedEmails.GetValueOrDefault(externalUserId, []);
    }
}

internal sealed class FakeInvitationSender : IInvitationSender
{
    public ConcurrentBag<(string Email, Uri Link)> Sent { get; } = [];

    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken)
    {
        Sent.Add((email, link));
        return Task.CompletedTask;
    }

    // The token of the accept link sent to someone who already has an account.
    public string TokenSentTo(string email)
    {
        var link = Sent.Single(sent => sent.Email == email).Link;
        return Uri.UnescapeDataString(link.Query["?token=".Length..]);
    }
}

// An email channel that is down for its first sends, then recovers.
internal sealed class FlakyInvitationSender(int failures) : IInvitationSender
{
    private int _attempts;

    public ConcurrentBag<(string Email, Uri Link)> Sent { get; } = [];

    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _attempts) <= failures)
        {
            throw new InvalidOperationException("The email channel is down for a moment.");
        }

        Sent.Add((email, link));
        return Task.CompletedTask;
    }
}
