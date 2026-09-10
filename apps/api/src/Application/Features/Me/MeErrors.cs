using Application.Results;

namespace Application.Features.Me;

public static class MeErrors
{
    public const string ExpiredCode = "invitation.expired";

    public static readonly Error EmailClaimRequired = Error.Forbidden(
        "email_claim.missing",
        "The access token must carry a verified 'email' claim. Add it to the Clerk JWT template.");

    public static readonly Error UnknownToken =
        Error.NotFound("invitation.unknown", "The invitation does not exist or is no longer pending.");

    // A 404 by default; the endpoint refines this one code to 410 Gone.
    public static readonly Error Expired =
        Error.NotFound(ExpiredCode, "The invitation has expired.");

    public static readonly Error EmailMismatch = Error.Forbidden(
        "invitation.email_mismatch",
        "The invitation was issued to a different email address.");
}
