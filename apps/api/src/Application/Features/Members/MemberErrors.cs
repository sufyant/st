using Application.Results;

namespace Application.Features.Members;

internal static class MemberErrors
{
    public static readonly Error Missing =
        Error.NotFound("member.missing", "The member does not exist in this tenant.");
}
