using SharedKernel;

namespace ControlPlane.Application;

internal static class Errors
{
    public static readonly Error InvitationNotFound = Error.NotFound("invitation.not_found", "The invitation was not found.");
}
