using Application.Results;

namespace Application.Features.Members;

internal static class MemberErrors
{
    public static readonly Error Missing =
        Error.NotFound("member.missing", "The member does not exist in this tenant.");

    public static readonly Error LastOwner = Error.Conflict(
        "member.last_owner",
        "The last owner of a tenant cannot be removed, disabled or demoted.");
}
