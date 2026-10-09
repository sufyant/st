using ControlPlane.Application.Ports;
using ControlPlane.Contracts;
using ControlPlane.Domain.Tenants;
using Microsoft.Extensions.Logging;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace ControlPlane.Application.Tenants;

// The steps and compensations of the tenant onboarding saga. Each runs in the new tenant, may run twice (S7), and returns its
// result as a message. A step that calls the identity provider takes no DbContext, so Wolverine opens no transaction around the
// call; its retries are configured from OnboardingSettings. A step that fails for good goes to the dead letter queue, and the saga
// hears of it through the fault Wolverine publishes.

public static class RegisterOwnerWithIdentityProviderHandler
{
    // Someone with an account gets our accept link; someone without one gets the provider's sign-up link, which leads to it.
    public static async Task<OwnerRegistered> HandleAsync(
        RegisterOwnerWithIdentityProvider step,
        IIdentityProvider identity,
        CancellationToken cancellationToken)
    {
        if (await identity.HasAccountAsync(step.Email, cancellationToken))
        {
            return new OwnerRegistered(step.TenantId, step.InvitationId, step.AcceptLink, null);
        }

        var invited = await identity.InviteAsync(step.Email, step.InvitationId, step.AcceptLink, cancellationToken);
        return new OwnerRegistered(step.TenantId, step.InvitationId, invited.Link, invited.Id);
    }
}

public static class ActivateTenantHandler
{
    public static void Configure(HandlerChain chain) => chain.OnAnyException().RetryTimes(3);

    // The pivot. The tenant is locked, so a repeated activation finds it active and announces nothing again. The tenant was created
    // by the system admin who invited its first owner.
    public static async Task<(TenantActivated?, OwnerInvitationReady?)> HandleAsync(
        ActivateTenant step,
        ITenantCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var tenant = await catalog.FindTenantForUpdateAsync(cancellationToken);
        if (!tenant.Activate())
        {
            return (null, null);
        }

        var invitation = await catalog.FindInvitationForUpdateAsync(step.InvitationId, cancellationToken)
            ?? throw new InvalidOperationException("A tenant is activated with its first owner's invitation.");
        var now = time.GetUtcNow();
        await catalog.SaveChangesAsync(cancellationToken);

        return (
            new TenantActivated(Guid.CreateVersion7(now), now, tenant.Id, tenant.Name, invitation.InvitedBy),
            new OwnerInvitationReady(Guid.CreateVersion7(now), now, tenant.Id, invitation.Id, invitation.Email, tenant.Name, step.Link));
    }
}

public static partial class CancelTenantHandler
{
    public static void Configure(HandlerChain chain) => chain.OnAnyException().RetryTimes(3);

    // An active tenant is past the pivot and is never cancelled: the compensation fails, and the saga needs attention.
    public static async Task<TenantCancelled> HandleAsync(
        CancelTenant step,
        ITenantCatalog catalog,
        ILogger<TenantOnboarding> logger,
        CancellationToken cancellationToken)
    {
        var tenant = await catalog.FindTenantForUpdateAsync(cancellationToken);
        if (tenant.Cancel(step.Reason))
        {
            LogCancelled(logger, step.TenantId, step.Reason);
        }
        else if (tenant.Status != TenantStatus.Cancelled)
        {
            throw new InvalidOperationException("An active tenant is not cancelled.");
        }

        (await catalog.FindInvitationForUpdateAsync(step.InvitationId, cancellationToken))?.Cancel();
        await catalog.SaveChangesAsync(cancellationToken);
        return new TenantCancelled(step.TenantId);
    }

    [LoggerMessage(EventName = "TenantCancelled", Level = LogLevel.Warning, Message = "Tenant {TenantId} is cancelled: {Reason}")]
    private static partial void LogCancelled(ILogger logger, Guid tenantId, string reason);
}

public static class CancelInvitationHandler
{
    public static void Configure(HandlerChain chain) => chain.OnAnyException().RetryTimes(3);

    public static async Task HandleAsync(CancelInvitation step, ITenantCatalog catalog, CancellationToken cancellationToken)
    {
        (await catalog.FindInvitationForUpdateAsync(step.InvitationId, cancellationToken))?.Cancel();
        await catalog.SaveChangesAsync(cancellationToken);
    }
}

public static class RevokeOwnerRegistrationHandler
{
    // An owner who already had an account was given no provider invitation, so there is nothing to revoke.
    public static async Task HandleAsync(RevokeOwnerRegistration step, IIdentityProvider identity, CancellationToken cancellationToken)
    {
        if (step.IdentityProviderInvitationId is { } invitationId)
        {
            await identity.RevokeInvitationAsync(invitationId, cancellationToken);
        }
    }
}

public static class OnboardingAlarmHandler
{
    public static void Handle(RaiseOnboardingAlarm alarm, OnboardingAlarm onboardingAlarm) => onboardingAlarm.Raise(alarm.TenantId);
}
