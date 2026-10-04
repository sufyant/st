using System.Security.Claims;
using ControlPlane.Application.Invitations;
using ControlPlane.Application.Members;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Reports;
using ControlPlane.Application.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using Wolverine;

namespace ControlPlane.Api;

/// <summary>
/// The module's endpoints, mapped into the host's route groups: <c>/v1</c>, the tenant routes (0015), and the admin routes
/// (0031). Each endpoint names the permission it needs (0030) and sends one command; the host maps its result (0032).
/// </summary>
public static class ControlPlaneEndpoints
{
    private static readonly Error InvitationNotFound = Error.NotFound("invitation.not_found", "The invitation was not found.");

    public static void MapControlPlaneEndpoints(
        this RouteGroupBuilder v1,
        RouteGroupBuilder tenant,
        RouteGroupBuilder admin,
        RouteGroupBuilder adminTenant)
    {
        MapRoles(tenant.MapGroup("/roles").RequireAuthorization(Permissions.RolesManage));
        MapMembers(tenant.MapGroup("/members").RequireAuthorization(Permissions.MembersManage));

        tenant.MapPost("/invitations", async (InvitationRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
                (await bus.InvokeAsync<Result<InvitationDetails>>(new CreateInvitation(user.Id(), request.Email, request.RoleId), cancellationToken))
                    .Map(ToResponse))
            .RequireAuthorization(Permissions.MembersInvite);

        // Accepting starts outside any tenant; the token leads to the tenant, and the invitation is accepted there (0029).
        v1.MapPost("/invitations/accept", async (
            AcceptInvitationRequest request,
            ClaimsPrincipal user,
            IInvitationDirectory invitations,
            IMessageBus bus,
            CancellationToken cancellationToken) =>
        {
            if (await invitations.FindTenantAsync(request.Token, cancellationToken) is not { } tenantId)
            {
                return (Result<AcceptedInvitationResponse>)InvitationNotFound;
            }

            var accepted = await bus.InvokeForTenantAsync<Result<InvitationAccepted>>(
                tenantId.ToString(), new AcceptInvitation(request.Token, user.Id()), cancellationToken);
            return accepted.Map(invitation => new AcceptedInvitationResponse(invitation.TenantSlug));
        });

        admin.MapGet("/tenants", async (int? page, int? pageSize, IMessageBus bus, CancellationToken cancellationToken) =>
                (await bus.InvokeAsync<Result<PagedList<TenantSummary>>>(
                    new ListTenants(page ?? 1, pageSize ?? PagedList<TenantSummary>.DefaultPageSize), cancellationToken))
                    .Map(tenants => tenants.Map(summary => new TenantSummaryResponse(summary.Id, summary.Slug, summary.Status, summary.MemberCount))))
            .RequireAuthorization(Permissions.SystemTenantsRead);

        adminTenant.MapPost("/invitations", async (InvitationRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
                (await bus.InvokeAsync<Result<InvitationDetails>>(
                    new CreateInvitationAsSystemAdmin(user.Id(), request.Email, request.RoleId), cancellationToken))
                    .Map(ToResponse))
            .RequireAuthorization(Permissions.SystemMembersInvite);
    }

    private static void MapRoles(RouteGroupBuilder roles)
    {
        roles.MapPost("", async (RoleRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            (await bus.InvokeAsync<Result<RoleDetails>>(new CreateRole(user.Id(), request.Name, request.Permissions), cancellationToken))
                .Map(ToResponse));

        roles.MapPut("/{roleId:guid}", async (Guid roleId, RoleRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            (await bus.InvokeAsync<Result<RoleDetails>>(new ChangeRole(user.Id(), roleId, request.Name, request.Permissions), cancellationToken))
                .Map(ToResponse));

        roles.MapDelete("/{roleId:guid}", (Guid roleId, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(new DeleteRole(user.Id(), roleId), cancellationToken));
    }

    private static void MapMembers(RouteGroupBuilder members)
    {
        members.MapPut("/{userId:guid}/role", (Guid userId, ChangeMemberRoleRequest request, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(new ChangeMemberRole(user.Id(), userId, request.RoleId), cancellationToken));

        members.MapDelete("/{userId:guid}", (Guid userId, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(new RemoveMember(user.Id(), userId), cancellationToken));
    }

    // The version group lets only signed-in users through, so the identity provider's user id is always there (0015).
    private static string Id(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The request has no signed-in user.");

    private static RoleResponse ToResponse(RoleDetails role) => new(role.Id, role.Name, role.BuiltIn, role.Permissions);

    private static InvitationResponse ToResponse(InvitationDetails invitation) =>
        new(invitation.Id, invitation.Email, invitation.RoleId, invitation.Status, invitation.ExpiresAt);
}
