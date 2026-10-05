using ControlPlane.Application.Invitations;
using ControlPlane.Application.Members;
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

    public static Task<(Result<InvitationDetails> Result, DeliverInvitation? Delivery)> InviteAsync(
        IServiceProvider services,
        Guid tenantId,
        string actorId,
        string email,
        Guid roleId) =>
        InTenant.RunAsync(services, tenantId, scope => CreateInvitationHandler.HandleAsync(
            new CreateInvitation(actorId, email, roleId),
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<InvitationSettings>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));

    // The delivery runs after the invitation is saved, in the invitation's tenant, the way the outbox hands it on (0029).
    public static Task DeliverAsync(IServiceProvider services, Guid tenantId, DeliverInvitation delivery) =>
        InTenant.ProcessAsync(services, tenantId, scope => DeliverInvitationHandler.HandleAsync(
            delivery,
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<IIdentityProvider>(),
            scope.GetRequiredService<IInvitationSender>(),
            scope.GetRequiredService<InvitationSettings>(),
            Cancellation));

    public static async Task<InvitationDetails> InviteAndDeliverAsync(IServiceProvider services, Guid tenantId, string actorId, string email, Guid roleId)
    {
        var (invitation, delivery) = await InviteAsync(services, tenantId, actorId, email, roleId);
        await DeliverAsync(services, tenantId, delivery.ShouldNotBeNull());

        return invitation.Value;
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

    public static Task<Result> ChangeMemberRoleAsync(IServiceProvider services, Guid tenantId, string actorId, Guid userId, Guid roleId) =>
        InTenant.RunAsync(services, tenantId, scope => ChangeMemberRoleHandler.HandleAsync(
            new ChangeMemberRole(actorId, userId, roleId), scope.GetRequiredService<ITenantCatalog>(), Cancellation));

    // Onboarding runs inside the tenant it creates, whose id the endpoint chooses (0026).
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
