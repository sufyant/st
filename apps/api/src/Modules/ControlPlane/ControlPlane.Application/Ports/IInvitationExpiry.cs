namespace ControlPlane.Application.Ports;

/// <summary>
/// Closes expired invitations in every tenant at once (0027, 0029), outside any tenant. It only moves pending invitations whose
/// expiry has passed to expired, and reveals nothing.
/// </summary>
public interface IInvitationExpiry
{
    Task CloseExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
