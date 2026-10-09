using Api.Authorization;

namespace Api.Tenants;

internal static class TenantRoutes
{
    public const string TenantIdParameter = "tenantId";

    // Tenant routes are /v1/tenants/{tenantId}/... (T1); a value that is not a GUID matches no route. Every endpoint in the group
    // runs only for a verified member (T7).
    public static RouteGroupBuilder MapTenant(this RouteGroupBuilder v1) =>
        v1.MapGroup($"/tenants/{{{TenantIdParameter}:guid}}")
            .WithMetadata(new TenantScopedEndpoint())
            .RequireAuthorization(AccessPolicies.TenantMember);
}

internal sealed class TenantScopedEndpoint;
