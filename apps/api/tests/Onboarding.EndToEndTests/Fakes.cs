using System.Collections.Concurrent;
using ControlPlane.Application.Ports;
using Notifications.Application;
using Notifications.Application.Ports;

namespace Onboarding.EndToEndTests;

// Clerk, as far as the application uses it. The owner signs up like any other user, and the provider verifies their email.
internal sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly ConcurrentDictionary<string, string[]> _verifiedEmails = new();

    public void SignUp(string externalUserId, string verifiedEmail) => _verifiedEmails[externalUserId] = [verifiedEmail];

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
