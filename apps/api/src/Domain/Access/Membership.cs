namespace Domain.Access;

public enum MembershipStatus
{
    Active
}

public sealed class Membership
{
    private Membership()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public IdentityUserId UserId { get; private set; } = null!;

    public Guid RoleId { get; private set; }

    public MembershipStatus Status { get; private set; }

    public static Membership Create(Guid id, Guid tenantId, IdentityUserId userId, Guid roleId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Membership ID cannot be empty.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("Role ID cannot be empty.", nameof(roleId));
        }

        ArgumentNullException.ThrowIfNull(userId);

        return new Membership
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            Status = MembershipStatus.Active
        };
    }
}
