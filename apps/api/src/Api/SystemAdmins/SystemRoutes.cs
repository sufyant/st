using Api.Authorization;

namespace Api.SystemAdmins;

// The system door (T5): a separate route group for system admins, with its own authorization policy. ControlPlane decides who
// passes, including the second factor (A6).
internal static class SystemRoutes
{
    public static RouteGroupBuilder MapSystem(this RouteGroupBuilder v1) =>
        v1.MapGroup("/system")
            .WithMetadata(new SystemEndpoint())
            .RequireAuthorization(AccessPolicies.SystemAdmin);
}

internal sealed class SystemEndpoint;
