namespace Api.Tenants;

internal static class TenantRoutes
{
    public const string SlugParameter = "tenantSlug";

    // Tenant-scoped routes are /v1/tenants/{slug}/... (0033); their own segment keeps every slug apart from the routes outside
    // tenants. Every endpoint in the group runs only for a verified member (0015).
    public static RouteGroupBuilder MapTenant(this RouteGroupBuilder v1) =>
        v1.MapGroup($"/tenants/{{{SlugParameter}}}")
            .WithMetadata(new TenantScopedEndpoint())
            .AddEndpointFilter<TenantRequirementFilter>();
}

internal sealed class TenantScopedEndpoint;
