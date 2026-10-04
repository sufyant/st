namespace Tenancy;

/// <summary>
/// The catalog lookups tenant resolution needs before any tenant is known (0015). Implemented by the module that owns the
/// catalog, so the host never knows it.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>The user's membership in the tenant with this slug, when the tenant is active and the user is its member.</summary>
    Task<TenantMembership?> FindMembershipAsync(string slug, string externalUserId, CancellationToken cancellationToken);

    /// <summary>The id of the tenant with this slug, whatever its status, for a system admin entering it (0031).</summary>
    Task<Guid?> FindTenantAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>A verified membership: the tenant and the permissions the member's role holds there (0030).</summary>
public sealed record TenantMembership(Guid TenantId, IReadOnlySet<string> Permissions);
