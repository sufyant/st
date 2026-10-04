namespace ControlPlane.Domain.Invitations;

internal enum InvitationStatus
{
    Pending,
    Accepted,

    // Written by the system job that closes invitations past their lifetime (0027); until then, the lifetime decides.
    Expired,
}
