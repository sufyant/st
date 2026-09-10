using System.Security.Claims;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Access;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Me;

public sealed record AcceptInvitationRequest(string Token);

public sealed record MyMembership(Guid TenantId, string TenantAlias, string TenantStatus);

public sealed record MyInvitation(
    Guid Id,
    string TenantAlias,
    string Email,
    string RoleCode,
    DateTimeOffset ExpiresAt);

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").RequireAuthorization();

        group.MapGet("/memberships", ListMembershipsAsync);
        group.MapGet("/invitations", ListInvitationsAsync);
        group.MapPost("/invitations/accept", AcceptAsync);

        return endpoints;
    }

    private static async Task<IResult> ListMembershipsAsync(
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var externalUserId = ExternalUserId.Create(user.FindFirstValue("sub")!);
        // The projection runs after materialisation: EF cannot translate value object access
        // inside a join result selector.
        var tenants = await dbContext.Memberships
            .AsNoTracking()
            .Where(membership => membership.ExternalUserId == externalUserId)
            .Join(
                dbContext.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (_, tenant) => tenant)
            .OrderBy(tenant => tenant.Alias)
            .ToListAsync(cancellationToken);
        var memberships = tenants
            .Select(tenant => new MyMembership(tenant.Id, tenant.Alias.Value, tenant.Status.ToString()))
            .ToList();

        return Results.Ok(memberships);
    }

    private static async Task<IResult> ListInvitationsAsync(
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmail(user, out var email))
        {
            return EmailClaimRequired();
        }

        var now = timeProvider.GetUtcNow();
        var rows = await dbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.Email == email
                                 && invitation.Status == InvitationStatus.Pending
                                 && invitation.ExpiresAt >= now)
            .Join(
                dbContext.Tenants,
                invitation => invitation.TenantId,
                tenant => tenant.Id,
                (invitation, tenant) => new { Invitation = invitation, Tenant = tenant })
            .ToListAsync(cancellationToken);
        var invitations = rows
            .Select(row => new MyInvitation(
                row.Invitation.Id,
                row.Tenant.Alias.Value,
                row.Invitation.Email.Value,
                row.Invitation.RoleCode,
                row.Invitation.ExpiresAt))
            .ToList();

        return Results.Ok(invitations);
    }

    private static async Task<IResult> AcceptAsync(
        AcceptInvitationRequest request,
        ControlPlaneDbContext dbContext,
        TenantDbContextFactory tenantDbContextFactory,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Results.NotFound();
        }

        var tokenHash = InvitationTokens.Hash(request.Token);
        var invitation = await dbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();

        if (invitation.IsExpired(now))
        {
            return Results.StatusCode(StatusCodes.Status410Gone);
        }

        if (!TryGetEmail(user, out var email))
        {
            return EmailClaimRequired();
        }

        if (email != invitation.Email)
        {
            return Results.Forbid();
        }

        var tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == invitation.TenantId,
            cancellationToken);

        if (tenant.Status is not TenantStatus.Active)
        {
            return Results.Conflict(new { error = $"Tenant is {tenant.Status}, not Active." });
        }

        var externalUserId = ExternalUserId.Create(user.FindFirstValue("sub")!);
        await using var tenantDbContext = tenantDbContextFactory.Create(tenant.DatabaseName.Value);
        var role = await tenantDbContext.Roles.SingleOrDefaultAsync(
            candidate => candidate.Code == invitation.RoleCode,
            cancellationToken);

        if (role is null)
        {
            return Results.Conflict(new { error = $"Role '{invitation.RoleCode}' no longer exists." });
        }

        // The tenant database is written before the control plane so that a failure in between
        // leaves no membership, and therefore no way in, until the retry completes.
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ExternalUserId == externalUserId,
            cancellationToken);

        if (tenantUser is null)
        {
            tenantUser = TenantUser.Create(Guid.CreateVersion7(), externalUserId, TenantUserStatus.Active);
            tenantDbContext.Users.Add(tenantUser);
        }
        else
        {
            tenantUser.Enable();
        }

        var hasRole = await tenantDbContext.UserRoles.AnyAsync(
            assignment => assignment.UserId == tenantUser.Id && assignment.RoleId == role.Id,
            cancellationToken);

        if (!hasRole)
        {
            tenantDbContext.UserRoles.Add(TenantUserRole.Create(tenantUser.Id, role.Id));
        }

        await tenantDbContext.SaveChangesAsync(cancellationToken);

        var hasMembership = await dbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id
                          && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            dbContext.Memberships.Add(Membership.Create(Guid.CreateVersion7(), tenant.Id, externalUserId));
        }

        invitation.Accept(externalUserId, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new MyMembership(tenant.Id, tenant.Alias.Value, tenant.Status.ToString()));
    }

    private static bool TryGetEmail(ClaimsPrincipal user, out EmailAddress email)
    {
        var claim = user.FindFirstValue("email");
        email = null!;

        if (string.IsNullOrWhiteSpace(claim))
        {
            return false;
        }

        try
        {
            email = EmailAddress.Create(claim);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IResult EmailClaimRequired() => Results.Problem(
        detail: "The access token must carry a verified 'email' claim. Add it to the Clerk JWT template.",
        statusCode: StatusCodes.Status403Forbidden);
}
