using System.Security.Claims;
using Api.Authorization;
using Domain.Access;
using Infrastructure.Access;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;

namespace Api.Features.Invitations;

public sealed record CreateInvitationRequest(string Email, string RoleCode);

public sealed record CreatedInvitation(
    Guid Id,
    string Email,
    string RoleCode,
    DateTimeOffset ExpiresAt,
    string Token);

public sealed record PendingInvitation(
    Guid Id,
    string Email,
    string RoleCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public static class InvitationEndpoints
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapTenantInvitations(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/{tenantAlias}/api/v1/invitations");

        group.MapPost("/", CreateAsync).RequirePermission(TenantPermissions.InvitationsManage);
        group.MapGet("/", ListAsync).RequirePermission(TenantPermissions.InvitationsManage);
        group.MapDelete("/{id:guid}", RevokeAsync).RequirePermission(TenantPermissions.InvitationsManage);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateInvitationRequest request,
        TenantContext tenantContext,
        ControlPlaneDbContext controlPlaneDbContext,
        TenantDbContext tenantDbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        EmailAddress email;

        try
        {
            email = EmailAddress.Create(request.Email);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        var roleExists = await tenantDbContext.Roles
            .AnyAsync(role => role.Code == request.RoleCode, cancellationToken);

        if (!roleExists)
        {
            return Results.BadRequest(new { error = $"Role '{request.RoleCode}' does not exist." });
        }

        var alreadyInvited = await controlPlaneDbContext.Invitations.AnyAsync(
            invitation => invitation.TenantId == tenantContext.TenantId
                          && invitation.Email == email
                          && invitation.Status == InvitationStatus.Pending,
            cancellationToken);

        if (alreadyInvited)
        {
            return Results.Conflict(new { error = $"'{email.Value}' already has a pending invitation." });
        }

        var token = InvitationTokens.Create();
        var invitation = Invitation.Create(
            Guid.CreateVersion7(),
            tenantContext.TenantId,
            email,
            request.RoleCode,
            InvitationTokens.Hash(token),
            ExternalUserId.Create(user.FindFirstValue("sub")!),
            timeProvider.GetUtcNow(),
            Lifetime);
        controlPlaneDbContext.Invitations.Add(invitation);
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/{tenantContext.Alias}/api/v1/invitations/{invitation.Id}",
            new CreatedInvitation(
                invitation.Id,
                invitation.Email.Value,
                invitation.RoleCode,
                invitation.ExpiresAt,
                token));
    }

    private static async Task<IResult> ListAsync(
        TenantContext tenantContext,
        ControlPlaneDbContext controlPlaneDbContext,
        CancellationToken cancellationToken)
    {
        var invitations = await controlPlaneDbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.TenantId == tenantContext.TenantId
                                 && invitation.Status == InvitationStatus.Pending)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Select(invitation => new PendingInvitation(
                invitation.Id,
                invitation.Email.Value,
                invitation.RoleCode,
                invitation.CreatedAt,
                invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(invitations);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        TenantContext tenantContext,
        ControlPlaneDbContext controlPlaneDbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invitation = await controlPlaneDbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.Id == id
                         && candidate.TenantId == tenantContext.TenantId
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Results.NotFound();
        }

        invitation.Revoke(timeProvider.GetUtcNow());
        await controlPlaneDbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
