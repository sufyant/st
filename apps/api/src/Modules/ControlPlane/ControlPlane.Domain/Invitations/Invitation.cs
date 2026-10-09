using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Domain.Invitations;

/// <summary>
/// An invitation to join a tenant with a role. It is single-use and expires. Its token is born with it and only the token's hash is
/// kept. Accepting it needs both the token and a verified email address of the accepting user that matches the invited one, so a
/// forwarded link does not let someone else in.
/// </summary>
internal sealed class Invitation : ITenantEntity
{
    public const int EmailMaxLength = 320;

    private Invitation(
        Guid id,
        Guid tenantId,
        string email,
        Guid roleId,
        Guid invitedBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string tokenHash)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        RoleId = roleId;
        InvitedBy = invitedBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        TokenHash = tokenHash;
        Status = InvitationStatus.Pending;
    }

    public Guid Id { get; private init; }

    public Guid TenantId { get; private init; }

    public string Email { get; private init; }

    public Guid RoleId { get; private init; }

    /// <summary>
    /// The SHA-256 hash of the token the invitation's link carries. Only an invitation from before tokens were born with their
    /// invitation has none; it can never be accepted.
    /// </summary>
    public string? TokenHash { get; private init; }

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
        TimeSpan lifetime,
        string token) =>
        new(id, tenantId, email.Trim(), role.Id, invitedBy, now, now + lifetime, InvitationToken.Hash(token));

    /// <summary>Withdraws a pending invitation, as when nobody could be told of it. A used or withdrawn one stays as it is.</summary>
    public bool Cancel()
    {
        if (Status != InvitationStatus.Pending)
        {
            return false;
        }

        Status = InvitationStatus.Cancelled;
        return true;
    }

    public Result Accept(IEnumerable<string> verifiedEmails, Guid userId, DateTimeOffset now)
    {
        if (Status != InvitationStatus.Pending)
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
