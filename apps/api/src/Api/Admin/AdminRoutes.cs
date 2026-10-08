using Api.Authorization;

namespace Api.Admin;

// The admin API (0031): a separate route group for system admins, with its own authorization policy and a second factor.
internal static class AdminRoutes
{
    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder v1) =>
        v1.MapGroup("/admin")
            .WithMetadata(new SystemAdminEndpoint())
            .RequireAuthorization(AccessPolicies.SystemAdmin);
}

internal sealed class SystemAdminEndpoint;
