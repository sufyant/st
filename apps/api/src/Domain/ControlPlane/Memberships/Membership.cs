using Domain.ControlPlane.Tenants;
using Domain.Shared;

namespace Domain.ControlPlane.Memberships;

public readonly record struct MembershipId(Guid Value)
{
    public static MembershipId New() => new(Guid.CreateVersion7());

    public static MembershipId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Membership ID cannot be empty.", nameof(value))
        : new MembershipId(value);
}

public sealed class Membership : Entity<MembershipId>
{
    private Membership()
    {
    }

    public TenantId TenantId { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public static Membership Create(TenantId tenantId, ExternalUserId externalUserId)
    {
        ArgumentNullException.ThrowIfNull(externalUserId);

        return new Membership
        {
            Id = MembershipId.New(),
            TenantId = tenantId,
            ExternalUserId = externalUserId
        };
    }
}
