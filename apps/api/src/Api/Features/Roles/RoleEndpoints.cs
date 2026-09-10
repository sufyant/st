using Api.Authorization;
using Api.Http;
using Application.Abstractions;
using Application.Features.Roles;

namespace Api.Features.Roles;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapTenantRoles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{tenantAlias}/api/v1/roles", async (
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(new ListRolesQuery(), cancellationToken)).ToOk())
            .RequirePermission(TenantPermissions.RolesRead)
            .WithTags("RoleEndpoints");

        return endpoints;
    }
}
