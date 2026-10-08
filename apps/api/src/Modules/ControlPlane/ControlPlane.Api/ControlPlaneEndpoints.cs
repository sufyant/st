using System.Security.Claims;
using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using Wolverine;

namespace ControlPlane.Api;

/// <summary>
/// The module's endpoints, mapped into the host's route groups: <c>/v1</c> and the admin routes. Each endpoint names the
/// permission it needs and sends one command; the host maps its result.
/// </summary>
public static class ControlPlaneEndpoints
{
    private static readonly Error InvitationNotFound = Error.NotFound("invitation.not_found", "The invitation was not found.");

    public static void MapControlPlaneEndpoints(
        this RouteGroupBuilder v1,
        RouteGroupBuilder admin)
    {
        // Accepting starts outside any tenant; the token leads to the tenant, and the invitation is accepted there. The
        // identity provider is asked first, so its call never runs while the acceptance holds the invitation locked.
        v1.MapPost("/invitations/accept", async (
            AcceptInvitationRequest request,
            ClaimsPrincipal user,
            IInvitationDirectory invitations,
            IIdentityProvider identity,
            IMessageBus bus,
            CancellationToken cancellationToken) =>
        {
            if (await invitations.FindTenantAsync(request.Token, cancellationToken) is not { } tenantId)
            {
                return (Result<AcceptedInvitationResponse>)InvitationNotFound;
            }

            var verifiedEmails = await identity.FindVerifiedEmailsAsync(user.Id(), cancellationToken);
            var accepted = await bus.InvokeForTenantAsync<Result<InvitationAccepted>>(
                tenantId.ToString(), new AcceptInvitation(request.Token, user.Id(), verifiedEmails), cancellationToken);
            return accepted.Map(invitation => new AcceptedInvitationResponse(invitation.TenantSlug));
        });

        // Onboarding runs inside the tenant it creates. The tenant's id is chosen here, by the server, never by the client.
        admin.MapPost("/tenants", async (CreateTenantRequest request, ClaimsPrincipal user, IMessageBus bus, TimeProvider time, CancellationToken cancellationToken) =>
                (await bus.InvokeForTenantAsync<Result<TenantDetails>>(
                    Guid.CreateVersion7(time.GetUtcNow()).ToString(),
                    new StartTenantOnboarding(user.Id(), request.Slug, request.OwnerEmail),
                    cancellationToken))
                    .Map(tenant => new TenantResponse(tenant.Id, tenant.Slug, tenant.Status)))
            .RequireAuthorization(Permissions.SystemTenantsCreate);
    }

    // The version group lets only signed-in users through, so the identity provider's user id is always there.
    private static string Id(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The request has no signed-in user.");
}
