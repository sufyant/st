using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ControlPlane.Application.Ports;
using Notifications.Application;
using Notifications.Application.Ports;

namespace Api.IntegrationTests;

// Clerk, as far as the application uses it.
internal sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly ConcurrentDictionary<string, string[]> _verifiedEmails = new();

    public void AddAccount(string externalUserId, params string[] verifiedEmails) => _verifiedEmails[externalUserId] = verifiedEmails;

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

// The email service at our port. Like Resend, it sends one email per idempotency key, however often it is asked (section 7).
internal sealed partial class FakeEmailChannel : IEmailChannel
{
    private readonly ConcurrentDictionary<string, EmailMessage> _sent = new();
    private int _failures;

    // Every request the application made, sent or refused.
    public ConcurrentQueue<(EmailMessage Email, long At)> Attempts { get; } = [];

    // The emails that went out.
    public IReadOnlyCollection<EmailMessage> Sent => [.. _sent.Values];

    // While down, every send fails.
    public bool IsDown { get; set; }

    // The next sends fail, then the channel recovers.
    public void FailNext(int sends) => _failures = sends;

    // Every send to this address fails, as when one recipient's mail server keeps refusing.
    public string? Refuses { get; set; }

    public Task SendAsync(EmailMessage email, CancellationToken cancellationToken)
    {
        Attempts.Enqueue((email, TimeProvider.System.GetTimestamp()));
        if (IsDown || email.To == Refuses || Interlocked.Decrement(ref _failures) >= 0)
        {
            throw new HttpRequestException("The email service is down.");
        }

        _sent.TryAdd(email.IdempotencyKey, email);
        return Task.CompletedTask;
    }

    public Uri LinkSentTo(string email) => new(Link().Match(Sent.Single(sent => sent.To == email).Text).Value);

    // The invitation code of the accept link sent to the address.
    public string CodeSentTo(string email)
    {
        var link = LinkSentTo(email);
        link.Query.ShouldStartWith("?code=");
        return Uri.UnescapeDataString(link.Query["?code=".Length..]);
    }

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex Link();
}
