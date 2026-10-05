namespace Notifications.Contracts;

/// <summary>
/// What the Notifications module offers other modules synchronously (0009). An email goes out during the call, so a caller can
/// send what must never sit in a stored message, such as an invitation link (0029).
/// </summary>
public interface INotificationsModule
{
    Task SendEmailAsync(EmailMessage email, CancellationToken cancellationToken);
}

/// <summary>A plain-text email to one address. Email templates are out of the template's scope for now (0045).</summary>
public sealed record EmailMessage(string To, string Subject, string Text);
