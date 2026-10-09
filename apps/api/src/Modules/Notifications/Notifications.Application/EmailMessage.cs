namespace Notifications.Application;

/// <summary>
/// A plain-text email to one address. Email templates are out of the template's scope for now. The email service sends one email
/// per idempotency key, so the same email asked for twice goes out once (O4).
/// </summary>
public sealed record EmailMessage(string To, string Subject, string Text, string IdempotencyKey);
