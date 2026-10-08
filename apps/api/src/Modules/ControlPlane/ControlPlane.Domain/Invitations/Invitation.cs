using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Domain.Invitations;

/// <summary>
/// An invitation to join a tenant with a role. It is single-use and expires. Its token is issued when it is delivered, so no
/// stored message ever carries one. Accepting it needs both the token and a verified email address of the accepting user that
/// matches the invited one, so a forwarded link does not let someone else in.
/// </summary>
internal sealed class Invitation
{
    public const int EmailMaxLength = 320;

    private Invitation(
        Guid id,
        Guid tenantId,
        string email,
        Guid roleId,
        Guid invitedBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        RoleId = roleId;
        InvitedBy = invitedBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = InvitationStatus.Pending;
    }

    public Guid Id { get; private init; }

    public Guid TenantId { get; private init; }

    public string Email { get; private init; }

    public Guid RoleId { get; private init; }

    /// <summary>The SHA-256 hash of the token the invitation's link carries; empty until the invitation is delivered.</summary>
    public string? TokenHash { get; private set; }

    public Guid InvitedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ExpiresAt { get; private init; }

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedBy { get; private set; }

    public static Invitation Create(
        Guid id,
        Guid tenantId,
        string email,
        Role role,
        Guid invitedBy,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new(id, tenantId, email.Trim(), role.Id, invitedBy, now, now + lifetime);

    /// <summary>
    /// Gives a pending invitation the token its link carries, keeping only the hash. A token is issued once: a delivery that arrives
    /// again must not replace the link already sent.
    /// </summary>
    public bool IssueToken(string token)
    {
        if (Status != InvitationStatus.Pending || TokenHash is not null)
        {
            return false;
        }

        TokenHash = InvitationToken.Hash(token);
        return true;
    }

    public Result Accept(IEnumerable<string> verifiedEmails, Guid userId, DateTimeOffset now)
    {
        if (Status == InvitationStatus.Accepted)
        {
            return Error.Conflict("invitation.not_pending", "The invitation has already been used.");
        }

        if (now >= ExpiresAt)
        {
            return Error.Conflict("invitation.expired", "The invitation has expired; ask for a new one.");
        }

        if (!verifiedEmails.Any(email => string.Equals(email.Trim(), Email, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Forbidden("invitation.email_mismatch", "The invitation was sent to an email address you have not verified.");
        }

        Status = InvitationStatus.Accepted;
        AcceptedAt = now;
        AcceptedBy = userId;
        return Result.Success();
    }
}
