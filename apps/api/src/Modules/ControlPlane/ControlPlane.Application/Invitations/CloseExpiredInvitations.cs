using ControlPlane.Application.Ports;

namespace ControlPlane.Application.Invitations;

/// <summary>
/// Closes every pending invitation whose time has run out, across all tenants (0027, 0029). A system job sends it; it runs outside
/// any tenant, like other catalog work (0016).
/// </summary>
public sealed record CloseExpiredInvitations;

public static class CloseExpiredInvitationsHandler
{
    public static Task HandleAsync(
        CloseExpiredInvitations command,
        IInvitationExpiry expiry,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        expiry.CloseExpiredAsync(time.GetUtcNow(), cancellationToken);
}
