using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Domain.Invitations;

/// <summary>
/// An invitation to join a tenant with a role (0029). It is single-use and expires. Accepting it needs both the token and a
/// verified email address of the accepting user that matches the invited one, so a forwarded link does not let someone else in.
/// </summary>
internal sealed class Invitation
{
    public const int EmailMaxLength = 320;

    private Invitation(
        Guid id,
        Guid tenantId,
        string email,
        Guid roleId,
        string tokenHash,
        Guid invitedBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        RoleId = roleId;
        TokenHash = tokenHash;
        InvitedBy = invitedBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = InvitationStatus.Pending;
    }

    public Guid Id { get; private init; }

    public Guid TenantId { get; private init; }

    public string Email { get; private init; }

    public Guid RoleId { get; private init; }

    public string TokenHash { get; private init; }

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
        string token,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new(id, tenantId, email.Trim(), role.Id, InvitationToken.Hash(token), invitedBy, now, now + lifetime);

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
