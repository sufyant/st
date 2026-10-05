using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Contracts;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace ControlPlane.Application.Tenants;

// The tenant onboarding saga (0025, 0026), orchestrated in one place. StartTenantOnboarding creates the tenant as provisioning; each
// further step is a durable message handled in the new tenant's transaction, and the tenant's status is the saga's state. A step
// that still fails after its retries goes to the dead letter queue, and the fault Wolverine publishes for it in the same tenant is
// compensated: the tenant becomes failed. The invitation's delivery is the last step and is not compensated.

/// <summary>Invites the tenant's first owner. The saga chose the invitation's id, so a step that arrives twice finds it.</summary>
public sealed record CreateFirstOwnerInvitation(Guid InvitationId, string OwnerEmail, Guid InvitedBy);

/// <summary>Makes the tenant active, then has the first owner's invitation delivered.</summary>
public sealed record ActivateTenant(Guid InvitationId);

public static class TenantOnboardingHandler
{
    public static void Configure(HandlerChain chain) => chain.OnAnyException().RetryTimes(3);

    public static async Task<ActivateTenant?> HandleAsync(
        CreateFirstOwnerInvitation step,
        ITenantCatalog catalog,
        InvitationSettings settings,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var tenant = await catalog.FindTenantForUpdateAsync(cancellationToken);
        if (tenant.Status != TenantStatus.Provisioning)
        {
            return null;
        }

        // A message may arrive twice (0024).
        if (await catalog.FindInvitationForUpdateAsync(step.InvitationId, cancellationToken) is null)
        {
            var now = time.GetUtcNow();
            catalog.Add(Invitation.Create(step.InvitationId, tenant.Id, step.OwnerEmail, BuiltInRoles.Owner, step.InvitedBy, now, settings.Lifetime));
            await catalog.SaveChangesAsync(cancellationToken);
        }

        return new ActivateTenant(step.InvitationId);
    }

    public static async Task<(DeliverInvitation?, TenantActivated?)> HandleAsync(
        ActivateTenant step,
        ITenantCatalog catalog,
        CancellationToken cancellationToken)
    {
        var tenant = await catalog.FindTenantForUpdateAsync(cancellationToken);
        if (!tenant.Activate())
        {
            return (null, null);
        }

        await catalog.SaveChangesAsync(cancellationToken);
        return (new DeliverInvitation(step.InvitationId), new TenantActivated(tenant.Id, tenant.Slug));
    }
}

// The compensation of a step that failed for good. Wolverine publishes its fault after moving the step to the dead letter queue,
// with the step's tenant; it does so on a best-effort basis, so the dead letter queue stays the record of what failed.
public static class FailTenantOnboardingHandler
{
    public static void Configure(HandlerChain chain) => chain.OnAnyException().RetryTimes(3);

    public static Task HandleAsync(Fault<CreateFirstOwnerInvitation> fault, ITenantCatalog catalog, CancellationToken cancellationToken) =>
        FailAsync(catalog, cancellationToken);

    public static Task HandleAsync(Fault<ActivateTenant> fault, ITenantCatalog catalog, CancellationToken cancellationToken) =>
        FailAsync(catalog, cancellationToken);

    private static async Task FailAsync(ITenantCatalog catalog, CancellationToken cancellationToken)
    {
        var tenant = await catalog.FindTenantForUpdateAsync(cancellationToken);
        if (tenant.Fail())
        {
            await catalog.SaveChangesAsync(cancellationToken);
        }
    }
}
