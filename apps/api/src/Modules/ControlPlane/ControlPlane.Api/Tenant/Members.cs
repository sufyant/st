using System.Security.Claims;
using System.Text.Json.Serialization;
using ControlPlane.Api.Authentication;
using ControlPlane.Application.Members;
using ControlPlane.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using Wolverine;

namespace ControlPlane.Api.Tenant;

/// <summary>The members of the tenant in the route.</summary>
public static class Members
{
    public static void Map(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/members", ListMembers).RequireAuthorization(Permissions.TenantMembersRead);
        tenant.MapPatch("/members/{userId:guid}", ChangeMemberRole).RequireAuthorization(Permissions.TenantMembersUpdate);
        tenant.MapDelete("/members/{userId:guid}", RemoveMember).RequireAuthorization(Permissions.TenantMembersDelete);
    }

    // The tenant comes from the route: the host has declared it, and row level security keeps the list to it.
    private static async Task<Result<ListPage<MemberResponse>>> ListMembers(
        int? page,
        int? pageSize,
        ListSort<ListMembersSort>? sort,
        string? q,
        ListFilter<MemberRole>? role,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var list = ListRequest.Create(page, pageSize, sort, q);
        if (!list.IsSuccess)
        {
            return list.Error;
        }

        var filter = ListFilter.Read(role, nameof(role));
        if (!filter.IsSuccess)
        {
            return filter.Error;
        }

        var members = await bus.InvokeAsync<Result<ListPage<MemberSummary>>>(new ListMembers(list.Value, filter.Value?.ToString()), cancellationToken);
        return members.Map(found => found.Map(ToResponse));
    }

    // Changing a role sets a state, so it takes no idempotency key (I1): the role the member already has answers 200 again.
    private static async Task<Result<MemberResponse>> ChangeMemberRole(
        Guid userId,
        ChangeMemberRoleRequest request,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var changed = await bus.InvokeAsync<Result<MemberSummary>>(new ChangeMemberRole(user.Id(), userId, request.Role.ToString()), cancellationToken);
        return changed.Map(ToResponse);
    }

    // Removing sets a state, so it takes no idempotency key (I1).
    private static async Task<Result> RemoveMember(Guid userId, ClaimsPrincipal user, IMessageBus bus, CancellationToken cancellationToken) =>
        await bus.InvokeAsync<Result>(new RemoveMember(user.Id(), userId), cancellationToken);

    private static MemberResponse ToResponse(MemberSummary member) =>
        new(member.UserId, member.Email, member.FirstName, member.LastName, member.Role, member.Deleted);
}

/// <summary>
/// The built-in role to give the member. The role is required: a request without one is refused, so it never falls back to the enum's
/// first value.
/// </summary>
public sealed record ChangeMemberRoleRequest([property: JsonRequired] MemberRole Role);

/// <summary>
/// A member of the tenant: the user's id, the copy of their email address and name, their role, Owner, Admin or Member, and whether the
/// identity provider deleted the user. A deleted user has an empty email address and empty names, and the screen shows "Deleted user",
/// as the audit logs say <c>deleted</c> (K3).
/// </summary>
public sealed record MemberResponse(Guid UserId, string? Email, string? FirstName, string? LastName, string Role, bool Deleted);

/// <summary>
/// A built-in role: the values of the role of a member and of an invitation, the role filter of the member and the invitation lists,
/// the role an invitation gives, and the role a member is given.
/// </summary>
[JsonConverter(typeof(ExactNameEnumConverter<MemberRole>))]
public enum MemberRole
{
    [JsonStringEnumMemberName("Owner")]
    Owner,

    [JsonStringEnumMemberName("Admin")]
    Admin,

    [JsonStringEnumMemberName("Member")]
    Member,
}
