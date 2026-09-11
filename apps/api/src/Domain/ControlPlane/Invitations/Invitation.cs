using Domain.Shared;

namespace Domain.ControlPlane.Invitations;

public enum InvitationStatus { Pending, Accepted, Revoked }

public sealed class Invitation
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public EmailAddress Email { get; private set; } = null!;

    public string RoleCode { get; private set; } = null!;

    public string TokenHash { get; private set; } = null!;

    public InvitationStatus Status { get; private set; }

    public ExternalUserId InvitedByExternalUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public ExternalUserId? AcceptedByExternalUserId { get; private set; }

    private Invitation()
    {
    }

    public static Invitation Create(
        Guid id,
        Guid tenantId,
        EmailAddress email,
        string roleCode,
        string tokenHash,
        ExternalUserId invitedBy,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Invitation ID cannot be empty.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(invitedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Invitation lifetime must be positive.", nameof(lifetime));
        }

        return new Invitation
        {
            Id = id,
            TenantId = tenantId,
            Email = email,
            RoleCode = roleCode,
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            InvitedByExternalUserId = invitedBy,
            CreatedAt = createdAt,
            ExpiresAt = createdAt + lifetime
        };
    }

    public bool IsExpired(DateTimeOffset now) => now > ExpiresAt;

    public void Accept(ExternalUserId acceptedBy, DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(acceptedBy);
        RequirePending();

        Status = InvitationStatus.Accepted;
        AcceptedByExternalUserId = acceptedBy;
        AcceptedAt = acceptedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RequirePending();

        Status = InvitationStatus.Revoked;
        AcceptedAt = null;
    }

    private void RequirePending()
    {
        if (Status is not InvitationStatus.Pending)
        {
            throw new InvalidOperationException($"Invitation is {Status}, not Pending.");
        }
    }
}
