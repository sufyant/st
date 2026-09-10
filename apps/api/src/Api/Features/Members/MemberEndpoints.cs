using Api.Authorization;
using Api.Tenants;
using Domain.Access;
using Domain.Access.Users;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Members;

public sealed record ReplaceRolesRequest(string[] RoleCodes);

public sealed record TenantMember(string ExternalUserId, string Status, string[] RoleCodes);

public static class MemberEndpoints
{
    private const string OwnerRoleCode = "owner";

    public static IEndpointRouteBuilder MapTenantMembers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/{tenantAlias}/api/v1/members");

        group.MapGet("/", ListAsync).RequirePermission(TenantPermissions.MembersRead);
        group.MapPut("/{externalUserId}/roles", ReplaceRolesAsync)
            .RequirePermission(TenantPermissions.RolesManage);
        group.MapDelete("/{externalUserId}", RevokeAsync)
            .RequirePermission(TenantPermissions.MembersManage);
        group.MapPost("/{externalUserId}/disable", DisableAsync)
            .RequirePermission(TenantPermissions.MembersManage);
        group.MapPost("/{externalUserId}/enable", EnableAsync)
            .RequirePermission(TenantPermissions.MembersManage);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var users = await tenantDbContext.Users.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = await tenantDbContext.UserRoles
            .AsNoTracking()
            .Join(
                tenantDbContext.Roles,
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, role) => new { assignment.UserId, role.Code })
            .ToListAsync(cancellationToken);
        var members = users
            .Select(user => new TenantMember(
                user.ExternalUserId.Value,
                user.Status.ToString(),
                assignments
                    .Where(assignment => assignment.UserId == user.Id)
                    .Select(assignment => assignment.Code!)
                    .OrderBy(code => code)
                    .ToArray()))
            .OrderBy(member => member.ExternalUserId)
            .ToList();

        return Results.Ok(members);
    }

    private static async Task<IResult> ReplaceRolesAsync(
        string externalUserId,
        ReplaceRolesRequest request,
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        var roles = await tenantDbContext.Roles
            .Where(role => request.RoleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);

        if (roles.Count != request.RoleCodes.Distinct().Count())
        {
            return Results.BadRequest(new { error = "One or more roles do not exist." });
        }

        if (!roles.Any(role => role.Code == OwnerRoleCode)
            && await IsLastOwnerAsync(tenantDbContext, user.Id, cancellationToken))
        {
            return LastOwner();
        }

        var existing = await tenantDbContext.UserRoles
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync(cancellationToken);
        tenantDbContext.UserRoles.RemoveRange(existing);
        tenantDbContext.UserRoles.AddRange(roles.Select(role => TenantUserRole.Create(user.Id, role.Id)));
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAsync(
        string externalUserId,
        TenantContext tenantContext,
        TenantDbContext tenantDbContext,
        ControlPlaneDbContext controlPlaneDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        if (await IsLastOwnerAsync(tenantDbContext, user.Id, cancellationToken))
        {
            return LastOwner();
        }

        var userId = ExternalUserId.Create(externalUserId);
        var memberships = await controlPlaneDbContext.Memberships
            .Where(membership => membership.TenantId == tenantContext.TenantId
                                 && membership.ExternalUserId == userId)
            .ToListAsync(cancellationToken);
        controlPlaneDbContext.Memberships.RemoveRange(memberships);
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

        var assignments = await tenantDbContext.UserRoles
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync(cancellationToken);
        tenantDbContext.UserRoles.RemoveRange(assignments);
        user.Disable();
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DisableAsync(
        string externalUserId,
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        if (await IsLastOwnerAsync(tenantDbContext, user.Id, cancellationToken))
        {
            return LastOwner();
        }

        user.Disable();
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> EnableAsync(
        string externalUserId,
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(tenantDbContext, externalUserId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        user.Enable();
        await tenantDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static Task<TenantUser?> FindUserAsync(
        TenantDbContext tenantDbContext,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        var userId = ExternalUserId.Create(externalUserId);

        return tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == userId,
            cancellationToken);
    }

    private static async Task<bool> IsLastOwnerAsync(
        TenantDbContext tenantDbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ownerUserIds = await tenantDbContext.UserRoles
            .Join(
                tenantDbContext.Roles.Where(role => role.Code == OwnerRoleCode),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, _) => assignment.UserId)
            .Join(
                tenantDbContext.Users.Where(user => user.Status == TenantUserStatus.Active),
                ownerId => ownerId,
                user => user.Id,
                (ownerId, _) => ownerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ownerUserIds.Count == 1 && ownerUserIds[0] == userId;
    }

    private static IResult LastOwner() => Results.Conflict(new
    {
        error = "The last owner of a tenant cannot be removed, disabled or demoted."
    });
}
