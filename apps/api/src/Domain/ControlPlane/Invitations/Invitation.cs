using Domain.ControlPlane.Tenants;
using Domain.Shared;

namespace Domain.ControlPlane.Invitations;

public enum InvitationStatus { Pending, Accepted, Revoked }

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.CreateVersion7());

    public static InvitationId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Invitation ID cannot be empty.", nameof(value))
        : new InvitationId(value);
}

public sealed class Invitation : Entity<InvitationId>, IAuditable
{
    public TenantId TenantId { get; private set; }

    public EmailAddress Email { get; private set; } = null!;

    public string RoleCode { get; private set; } = null!;

    public string TokenHash { get; private set; } = null!;

    public InvitationStatus Status { get; private set; }

    public ExternalUserId InvitedByExternalUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public ExternalUserId? AcceptedByExternalUserId { get; private set; }

    private Invitation()
    {
    }

    public static Invitation Create(
        TenantId tenantId,
        EmailAddress email,
        string roleCode,
        string tokenHash,
        ExternalUserId invitedBy,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(invitedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new Invitation
        {
            Id = InvitationId.New(),
            TenantId = tenantId,
            Email = email,
            RoleCode = roleCode,
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            InvitedByExternalUserId = invitedBy,
            ExpiresAt = expiresAt
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
