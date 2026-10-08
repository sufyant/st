using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Wolverine;

namespace ControlPlane.IntegrationTests;

// The module's handlers, called in their tenant with the services Wolverine would pass them.
internal static class Handlers
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // The delivery runs after the invitation is saved, in the invitation's tenant, the way the outbox hands it on.
    public static Task DeliverAsync(IServiceProvider services, Guid tenantId, DeliverInvitation delivery) =>
        InTenant.ProcessAsync(services, tenantId, scope => DeliverInvitationHandler.HandleAsync(
            delivery,
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<IIdentityProvider>(),
            scope.GetRequiredService<IInvitationSender>(),
            scope.GetRequiredService<InvitationSettings>(),
            Cancellation));

    // A tenant's first owner invited the way onboarding does it: the invitation step, then the activation, which hands on the
    // invitation's delivery. The tenant is active afterwards, and the invitation is not delivered yet.
    public static async Task<(Guid TenantId, string Slug, Guid InvitationId, DeliverInvitation Delivery)> InviteFirstOwnerAsync(
        IServiceProvider services,
        string ownerEmail)
    {
        var tenant = await Catalog.AddTenantAsync(services, Domain.Tenants.TenantStatus.Provisioning);
        var admin = await Catalog.AddUserAsync(services);
        var invitationId = Guid.CreateVersion7();
        var activate = await InviteFirstOwnerAsync(services, tenant.Id, new CreateFirstOwnerInvitation(invitationId, ownerEmail, admin.Id));
        var (delivery, _) = await ActivateAsync(services, tenant.Id, activate.ShouldNotBeNull());

        return (tenant.Id, tenant.Slug, invitationId, delivery.ShouldNotBeNull());
    }

    public static async Task<(Guid TenantId, string Slug, Guid InvitationId)> InviteAndDeliverAsync(IServiceProvider services, string ownerEmail)
    {
        var (tenantId, slug, invitationId, delivery) = await InviteFirstOwnerAsync(services, ownerEmail);
        await DeliverAsync(services, tenantId, delivery);

        return (tenantId, slug, invitationId);
    }

    // Accepting starts outside any tenant: the token leads to the tenant, the identity provider is asked for the user's verified
    // email addresses, and then the invitation is accepted in its tenant.
    public static async Task<Result<InvitationAccepted>> AcceptAsync(IServiceProvider services, string token, string userId)
    {
        Guid? tenantId;
        IReadOnlyList<string> verifiedEmails;
        await using (var scope = services.CreateAsyncScope())
        {
            tenantId = await scope.ServiceProvider.GetRequiredService<IInvitationDirectory>().FindTenantAsync(token, Cancellation);
            verifiedEmails = await scope.ServiceProvider.GetRequiredService<IIdentityProvider>().FindVerifiedEmailsAsync(userId, Cancellation);
        }

        return await InTenant.RunAsync(services, tenantId!.Value, scope => AcceptInvitationHandler.HandleAsync(
            new AcceptInvitation(token, userId, verifiedEmails),
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));
    }

    // Onboarding runs inside the tenant it creates, whose id the endpoint chooses.
    public static Task<(Result<TenantDetails> Result, CreateFirstOwnerInvitation? Next)> StartOnboardingAsync(
        IServiceProvider services,
        Guid tenantId,
        string adminId,
        string slug,
        string ownerEmail) =>
        InTenant.RunAsync(services, tenantId, scope => StartTenantOnboardingHandler.HandleAsync(
            new StartTenantOnboarding(adminId, slug, ownerEmail),
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));

    public static Task<ActivateTenant?> InviteFirstOwnerAsync(IServiceProvider services, Guid tenantId, CreateFirstOwnerInvitation step) =>
        InTenant.ProcessAsync(services, tenantId, scope => TenantOnboardingHandler.HandleAsync(
            step,
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<InvitationSettings>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));

    public static Task<(DeliverInvitation? Delivery, TenantActivated? Activated)> ActivateAsync(IServiceProvider services, Guid tenantId, ActivateTenant step) =>
        InTenant.ProcessAsync(services, tenantId, scope => TenantOnboardingHandler.HandleAsync(
            step,
            scope.GetRequiredService<ITenantCatalog>(),
            Cancellation));

    // The fault Wolverine publishes once a step has gone to the dead letter queue.
    public static Task FailOnboardingAsync(IServiceProvider services, Guid tenantId, ActivateTenant step) =>
        InTenant.ProcessAsync(services, tenantId, scope => FailTenantOnboardingHandler.HandleAsync(
            new Fault<ActivateTenant>(
                step,
                ExceptionInfo.From(new InvalidOperationException("The step failed."), includeMessage: false, includeStackTrace: false),
                4,
                DateTimeOffset.UtcNow,
                null,
                Guid.Empty,
                tenantId.ToString(),
                null,
                new Dictionary<string, string?>()),
            scope.GetRequiredService<ITenantCatalog>(),
            Cancellation));

    public static string TokenOf(Uri link) =>
        Uri.UnescapeDataString(link.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("token=", StringComparison.Ordinal))["token=".Length..]);
}
