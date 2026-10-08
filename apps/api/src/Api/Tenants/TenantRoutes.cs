using Api.Authorization;

namespace Api.Tenants;

internal static class TenantRoutes
{
    public const string SlugParameter = "tenantSlug";

    // Tenant-scoped routes are /v1/tenants/{slug}/...; their own segment keeps every slug apart from the routes outside
    // tenants. Every endpoint in the group runs only for a verified member.
    public static RouteGroupBuilder MapTenant(this RouteGroupBuilder v1) =>
        v1.MapGroup($"/tenants/{{{SlugParameter}}}")
            .WithMetadata(new TenantScopedEndpoint())
            .RequireAuthorization(AccessPolicies.TenantMember);
}

internal sealed class TenantScopedEndpoint;
