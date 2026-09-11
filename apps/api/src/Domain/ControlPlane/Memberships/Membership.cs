using Domain.Shared;

namespace Domain.ControlPlane.Memberships;

public sealed class Membership
{
    private Membership()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public static Membership Create(Guid id, Guid tenantId, ExternalUserId externalUserId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Membership ID cannot be empty.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(externalUserId);

        return new Membership
        {
            Id = id,
            TenantId = tenantId,
            ExternalUserId = externalUserId
        };
    }
}
