using Application.Abstractions;
using Application.Results;
using Domain.Authorization;
using Domain.ControlPlane.Invitations;
using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Infrastructure.Access;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Me;

public sealed record AcceptInvitationCommand(string Token) : ICommand<MyMembership>;

public sealed class AcceptInvitationHandler(
    ControlPlaneDbContext dbContext,
    TenantDbContextFactory tenantDbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<AcceptInvitationCommand, MyMembership>
{
    public async Task<Result<MyMembership>> HandleAsync(
        AcceptInvitationCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result<MyMembership>.Failure(MeErrors.UnknownToken);
        }

        var tokenHash = InvitationTokens.Hash(request.Token);
        var invitation = await dbContext.Invitations.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash
                         && candidate.Status == InvitationStatus.Pending,
            cancellationToken);

        if (invitation is null)
        {
            return Result<MyMembership>.Failure(MeErrors.UnknownToken);
        }

        var now = timeProvider.GetUtcNow();

        if (invitation.IsExpired(now))
        {
            return Result<MyMembership>.Failure(MeErrors.Expired);
        }

        if (!currentUser.TryGetEmail(out var email))
        {
            return Result<MyMembership>.Failure(MeErrors.EmailClaimRequired);
        }

        if (email != invitation.Email)
        {
            return Result<MyMembership>.Failure(MeErrors.EmailMismatch);
        }

        var tenant = await dbContext.Tenants.SingleAsync(
            candidate => candidate.Id == invitation.TenantId,
            cancellationToken);

        if (tenant.Status is not TenantStatus.Active)
        {
            return Result<MyMembership>.Failure(Error.Conflict(
                "tenant.not_active",
                $"Tenant is {tenant.Status}, not Active."));
        }

        var externalUserId = currentUser.Id;
        await using var tenantDbContext = tenantDbContextFactory.Create(tenant.DatabaseName.Value);
        var role = await tenantDbContext.Roles.SingleOrDefaultAsync(
            candidate => candidate.Code == invitation.RoleCode,
            cancellationToken);

        if (role is null)
        {
            return Result<MyMembership>.Failure(Error.Conflict(
                "role.missing",
                $"Role '{invitation.RoleCode}' no longer exists."));
        }

        // This context is opened by the handler rather than resolved from the request, because the
        // user-scoped surface has no tenant in scope, so the unit of work behavior cannot commit it.
        // The tenant database is written first so that a failure in between leaves no membership,
        // and therefore no way in, until the retry completes.
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ExternalUserId == externalUserId,
            cancellationToken);

        if (tenantUser is null)
        {
            tenantUser = User.Create(externalUserId, UserStatus.Active);
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
            tenantDbContext.UserRoles.Add(UserRole.Create(tenantUser.Id, role.Id));
        }

        await tenantDbContext.SaveChangesAsync(cancellationToken);

        var hasMembership = await dbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id
                          && membership.ExternalUserId == externalUserId,
            cancellationToken);

        if (!hasMembership)
        {
            dbContext.Memberships.Add(Membership.Create(tenant.Id, externalUserId));
        }

        invitation.Accept(externalUserId, now);

        return Result<MyMembership>.Success(
            new MyMembership(tenant.Id.Value, tenant.Alias.Value, tenant.Status.ToString()));
    }
}
