namespace ControlPlane.Domain.Tenants;

/// <summary>A user's membership in a tenant; authorization comes from here, not from the user record (0029, 0030).</summary>
internal sealed class Membership(Guid tenantId, Guid userId)
{
    public Guid TenantId { get; private init; } = tenantId;

    public Guid UserId { get; private init; } = userId;
}
