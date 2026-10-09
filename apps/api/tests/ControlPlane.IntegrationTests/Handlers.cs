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

    // Accepting starts outside any tenant: the invitation code names the tenant, the identity provider is asked for the user's
    // verified email addresses, and then the invitation is found by its secret in that tenant.
    public static async Task<Result<InvitationAccepted>> AcceptAsync(IServiceProvider services, string code, string userId)
    {
        IReadOnlyList<string> verifiedEmails;
        await using (var scope = services.CreateAsyncScope())
        {
            verifiedEmails = await scope.ServiceProvider.GetRequiredService<IIdentityProvider>().FindVerifiedEmailsAsync(userId, Cancellation);
        }

        return await InTenant.RunAsync(services, TenantIdOf(code), scope => AcceptInvitationHandler.HandleAsync(
            new AcceptInvitation(SecretOf(code), userId, verifiedEmails),
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
            new StartTenantOnboarding(adminId, "Acme Ltd", slug, ownerEmail),
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

    // The invitation code an accept link carries: `<tenantId>.<secret>`.
    public static string CodeOf(Uri link) =>
        Uri.UnescapeDataString(link.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("code=", StringComparison.Ordinal))["code=".Length..]);

    public static Guid TenantIdOf(string code) => Guid.Parse(code[..code.IndexOf('.', StringComparison.Ordinal)]);

    public static string SecretOf(string code) => code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
