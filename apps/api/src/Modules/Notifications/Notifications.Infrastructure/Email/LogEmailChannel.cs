using Microsoft.Extensions.Logging;
using Notifications.Application.Ports;
using Notifications.Contracts;

namespace Notifications.Infrastructure.Email;

// Writes email to the log instead of sending it, in Development without Resend. An email may carry a credential such as an
// invitation link, so this channel is never used anywhere else.
internal sealed partial class LogEmailChannel(ILogger<LogEmailChannel> logger) : IEmailChannel
{
    public Task SendAsync(EmailMessage email, CancellationToken cancellationToken)
    {
        LogEmail(logger, email.To, email.Subject, email.Text);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email to {To} is not sent in Development: {Subject}\n{Text}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string text);
}
