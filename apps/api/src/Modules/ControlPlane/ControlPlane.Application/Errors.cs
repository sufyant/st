using SharedKernel;

namespace ControlPlane.Application;

internal static class Errors
{
    public static readonly Error RoleNotFound = Error.NotFound("role.not_found", "The role was not found.");

    public static readonly Error MembershipNotFound = Error.NotFound("membership.not_found", "The member was not found.");

    public static readonly Error InvitationNotFound = Error.NotFound("invitation.not_found", "The invitation was not found.");
}
