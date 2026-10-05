namespace ControlPlane.Domain.Invitations;

internal enum InvitationStatus
{
    Pending,
    Accepted,

    // Written by the system job that closes invitations past their lifetime (0027); before it runs, the lifetime decides.
    Expired,
}
