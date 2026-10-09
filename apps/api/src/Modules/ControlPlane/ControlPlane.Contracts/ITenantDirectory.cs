namespace ControlPlane.Contracts;

/// <summary>
/// The catalog lookup tenant resolution needs before any tenant is known: the host asks it on every tenant route (T7).
/// </summary>
public interface ITenantDirectory
{
    /// <summary>The user's membership in the tenant with this id, when the tenant is active and the user is its member.</summary>
    Task<TenantMembership?> FindMembershipAsync(Guid tenantId, string externalUserId, CancellationToken cancellationToken);
}

/// <summary>A verified membership: the tenant and the permissions the member's role holds there.</summary>
public sealed record TenantMembership(Guid TenantId, IReadOnlySet<string> Permissions);
