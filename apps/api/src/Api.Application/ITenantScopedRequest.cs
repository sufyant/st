namespace Api.Application;

/// A request that acts on a specific tenant must implement this so
/// PermissionBehavior can verify the caller's authenticated tenant_id
/// claim actually matches the tenant being acted on -- holding a
/// permission SOMEWHERE is not the same as holding it for THIS tenant.
public interface ITenantScopedRequest
{
    Guid TenantId { get; }
}
