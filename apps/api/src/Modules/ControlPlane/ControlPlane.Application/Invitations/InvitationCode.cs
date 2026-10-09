namespace ControlPlane.Application.Invitations;

/// <summary>
/// The one value an invitation link carries: <c>&lt;tenantId&gt;.&lt;secret&gt;</c>. The tenant id is not secret; it names the
/// tenant to declare before the invitation is looked up by the hash of the secret. The secret, and the verified email address,
/// give the right to accept.
/// </summary>
public sealed record InvitationCode(Guid TenantId, string Secret)
{
    private const char Separator = '.';

    /// <summary>The code, or null when it has no valid tenant id or no secret; a caller answers that as an unknown invitation.</summary>
    public static InvitationCode? Parse(string? code) =>
        code?.Split(Separator) is [var tenantId, { Length: > 0 } secret] && Guid.TryParseExact(tenantId, "D", out var tenant)
            ? new InvitationCode(tenant, secret)
            : null;

    public override string ToString() => $"{TenantId}{Separator}{Secret}";
}
