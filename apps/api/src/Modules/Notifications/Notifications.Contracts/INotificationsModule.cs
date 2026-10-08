namespace Notifications.Contracts;

/// <summary>
/// What the Notifications module offers other modules synchronously. An email goes out during the call, so a caller can
/// send what must never sit in a stored message, such as an invitation link.
/// </summary>
public interface INotificationsModule
{
    Task SendEmailAsync(EmailMessage email, CancellationToken cancellationToken);
}

/// <summary>A plain-text email to one address. Email templates are out of the template's scope for now.</summary>
public sealed record EmailMessage(string To, string Subject, string Text);
