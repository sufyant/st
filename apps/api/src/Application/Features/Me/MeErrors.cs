using Application.Results;

namespace Application.Features.Me;

public static class MeErrors
{
    public const string ExpiredCode = "invitation.expired";

    public const string CredentialUnavailableCode = "tenant.credential_unavailable";

    public static readonly Error EmailClaimRequired = Error.Forbidden(
        "email_claim.missing",
        "The access token must carry a verified 'email' claim. Add it to the Clerk JWT template.");

    public static readonly Error UnknownToken =
        Error.NotFound("invitation.unknown", "The invitation does not exist or is no longer pending.");

    // A 404 by default; the endpoint refines this one code to 410 Gone.
    public static readonly Error Expired =
        Error.NotFound(ExpiredCode, "The invitation has expired.");

    // A 409 by default; the endpoint refines this one code to 503, the same answer the tenant
    // surface gives when a tenant has no usable database credential yet.
    public static readonly Error CredentialUnavailable = Error.Conflict(
        CredentialUnavailableCode,
        "The tenant is not ready to accept members yet.");

    public static readonly Error EmailMismatch = Error.Forbidden(
        "invitation.email_mismatch",
        "The invitation was issued to a different email address.");
}
