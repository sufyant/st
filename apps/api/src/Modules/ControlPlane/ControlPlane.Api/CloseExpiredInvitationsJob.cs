using ControlPlane.Application.Invitations;
using Wolverine;

namespace ControlPlane.Api;

/// <summary>
/// Hangfire's hourly trigger for closing expired invitations (0027, 0029). The work is a command, so it passes the host's pipeline
/// like any other.
/// </summary>
public sealed class CloseExpiredInvitationsJob(IMessageBus bus)
{
    public Task RunAsync(CancellationToken cancellationToken) => bus.InvokeAsync(new CloseExpiredInvitations(), cancellationToken);
}
