using Api.Authorization;
using Api.Http;
using Application.Abstractions;
using Application.Features.Members;

namespace Api.Features.Members;

public sealed record ReplaceRoleRequest(string RoleCode);

public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapTenantMembers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/{tenantAlias}/api/v1/members")
            .WithTags("MemberEndpoints");

        group.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
                (await mediator.SendAsync(new ListMembersQuery(), cancellationToken)).ToOk())
            .RequirePermission(TenantPermissions.MembersRead);

        group.MapPut("/{externalUserId}/role", async (
                    string externalUserId,
                    ReplaceRoleRequest request,
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(
                    new ReplaceMemberRoleCommand(externalUserId, request.RoleCode),
                    cancellationToken)).ToNoContent())
            .RequirePermission(TenantPermissions.RolesManage);

        group.MapDelete("/{externalUserId}", async (
                    string externalUserId,
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(
                    new RevokeMemberCommand(externalUserId),
                    cancellationToken)).ToNoContent())
            .RequirePermission(TenantPermissions.MembersManage);

        group.MapPost("/{externalUserId}/disable", async (
                    string externalUserId,
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(
                    new DisableMemberCommand(externalUserId),
                    cancellationToken)).ToNoContent())
            .RequirePermission(TenantPermissions.MembersManage);

        group.MapPost("/{externalUserId}/enable", async (
                    string externalUserId,
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                (await mediator.SendAsync(
                    new EnableMemberCommand(externalUserId),
                    cancellationToken)).ToNoContent())
            .RequirePermission(TenantPermissions.MembersManage);

        return endpoints;
    }
}
