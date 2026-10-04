using ControlPlane.Application.Ports;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Infrastructure;

// Writes the invitation link to the log until the Notifications module sends it as email through Resend (0029, 0037). The link
// carries the invitation token, so this sender is used in Development only.
internal sealed partial class LoggingInvitationSender(ILogger<LoggingInvitationSender> logger) : IInvitationSender
{
    public Task SendAsync(string email, Uri link, CancellationToken cancellationToken)
    {
        LogInvitation(logger, email, link);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invitation email to {Email} is not sent yet; its link is {Link}")]
    private static partial void LogInvitation(ILogger logger, string email, Uri link);
}
