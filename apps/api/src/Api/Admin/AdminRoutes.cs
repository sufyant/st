using Api.Authorization;
using Api.Tenants;

namespace Api.Admin;

// The admin API (0031): a separate route group for system admins, with its own authorization policy and a second factor.
// Inside it, /tenants/{slug}/... enters a tenant's context without membership: RLS applies as usual, and every entry is recorded.
internal static class AdminRoutes
{
    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder v1) =>
        v1.MapGroup("/admin")
            .WithMetadata(new SystemAdminEndpoint())
            .RequireAuthorization(AccessPolicies.SystemAdmin);

    public static RouteGroupBuilder MapAdminTenant(this RouteGroupBuilder admin) =>
        admin.MapGroup($"/tenants/{{{TenantRoutes.SlugParameter}}}")
            .WithMetadata(new AdminTenantScopedEndpoint())
            .RequireAuthorization(AccessPolicies.AdminTenant)
            .AddEndpointFilter<SystemAdminEntryFilter>();
}

internal sealed class SystemAdminEndpoint;

internal sealed class AdminTenantScopedEndpoint;
