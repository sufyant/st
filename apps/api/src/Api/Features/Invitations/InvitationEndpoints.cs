using Api.Authorization;
using Api.Http;
using Application.Abstractions;
using Application.Features.Invitations;

namespace Api.Features.Invitations;

public sealed record CreateInvitationRequest(string Email, string RoleCode);

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapTenantInvitations(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/{tenantAlias}/api/v1/invitations")
            .WithTags("InvitationEndpoints");

        group.MapPost("/", async (
                    CreateInvitationRequest request,
                    IMediator mediator,
                    TenantContext tenantContext,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(
                        new CreateInvitationCommand(request.Email, request.RoleCode),
                        cancellationToken))
                    .ToCreated(invitation =>
                        $"/{tenantContext.Alias}/api/v1/invitations/{invitation.Id}"))
            .RequirePermission(TenantPermissions.InvitationsManage);

        group.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
                (await mediator.SendAsync(new ListPendingInvitationsQuery(), cancellationToken)).ToOk())
            .RequirePermission(TenantPermissions.InvitationsManage);

        group.MapDelete("/{id:guid}", async (
                    Guid id,
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(new RevokeInvitationCommand(id), cancellationToken)).ToNoContent())
            .RequirePermission(TenantPermissions.InvitationsManage);

        return endpoints;
    }
}
