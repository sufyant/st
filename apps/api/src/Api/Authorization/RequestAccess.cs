using ControlPlane.Contracts;

namespace Api.Authorization;

// What the caller of this request may do, resolved once per request with the tenant: their membership on a
// tenant route, their system permissions on a system route.
internal sealed class RequestAccess
{
    public TenantMembership? Membership { get; set; }

    public IReadOnlySet<string>? SystemPermissions { get; set; }

    // The tenant and system pools never share a permission, so one set never satisfies the other's checks.
    public bool Has(string permission) =>
        Membership?.Permissions.Contains(permission) == true || SystemPermissions?.Contains(permission) == true;
}
