namespace Tenancy;

/// <summary>
/// The catalog lookups tenant resolution needs before any tenant is known. Implemented by the module that owns the
/// catalog, so the host never knows it.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>The user's membership in the tenant with this slug, when the tenant is active and the user is its member.</summary>
    Task<TenantMembership?> FindMembershipAsync(string slug, string externalUserId, CancellationToken cancellationToken);
}

/// <summary>A verified membership: the tenant and the permissions the member's role holds there.</summary>
public sealed record TenantMembership(Guid TenantId, IReadOnlySet<string> Permissions);
