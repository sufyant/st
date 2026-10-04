namespace Tenancy;

/// <summary>Resolves a tenant slug for a user: the tenant's id when the tenant is active and the user is its member (0015).</summary>
public interface ITenantDirectory
{
    Task<Guid?> FindMemberTenantAsync(string slug, string externalUserId, CancellationToken cancellationToken);
}
