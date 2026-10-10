using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Wolverine;
using Wolverine.Persistence;

namespace ControlPlane.IntegrationTests;

// The module's handlers, called in their tenant with the services Wolverine would pass them.
internal static class Handlers
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Onboarding runs inside the tenant it creates, whose id the endpoint chooses. The saga it starts is inserted in the same
    // transaction, as Wolverine inserts a returned Insert<T>.
    public static Task<StartedOnboarding> StartOnboardingAsync(
        IServiceProvider services,
        Guid tenantId,
        string adminId,
        string slug,
        string ownerEmail) =>
        InTenant.RunAsync(services, tenantId, async scope =>
        {
            var (result, onboarding, activate, timeout) = await StartTenantOnboardingHandler.HandleAsync(
                new StartTenantOnboarding(adminId, "Acme Ltd", slug, ownerEmail, Guid.NewGuid().ToString()),
                scope.GetRequiredService<ITenantCatalog>(),
                scope.GetRequiredService<InvitationSettings>(),
                scope.GetRequiredService<OnboardingSettings>(),
                scope.GetRequiredService<TimeProvider>(),
                Cancellation);
            if (onboarding is not null)
            {
                scope.GetRequiredService<CatalogDbContext>().Add(onboarding.Entity);
            }

            return new StartedOnboarding(result, onboarding, activate, timeout);
        });

    public static Task<(TenantActivated? Activated, OwnerInvitationReady? Ready)> ActivateAsync(IServiceProvider services, Guid tenantId, ActivateTenant step) =>
        InTenant.RunAsync(services, tenantId, scope => ActivateTenantHandler.HandleAsync(
            step,
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));

    public static Task<TenantCancelled?> CancelTenantAsync(IServiceProvider services, Guid tenantId, CancelTenant step) =>
        InTenant.RunAsync(services, tenantId, scope => CancelTenantHandler.HandleAsync(
            step,
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<ILogger<TenantOnboarding>>(),
            Cancellation));

    public static Task CancelInvitationAsync(IServiceProvider services, Guid tenantId, CancelInvitation step) =>
        InTenant.RunAsync(services, tenantId, scope => CancelInvitationHandler.HandleAsync(step, scope.GetRequiredService<ITenantCatalog>(), Cancellation));

    // A tenant onboarded up to its pivot, the way the saga drives it: started and activated. Returns what the invitation email is
    // sent from.
    public static async Task<(Guid TenantId, string Slug, OwnerInvitationReady Ready)> OnboardAsync(IServiceProvider services, string ownerEmail)
    {
        var admin = await Catalog.AddUserAsync(services);
        var tenantId = Guid.CreateVersion7();
        var slug = Unique.Slug();
        var started = await StartOnboardingAsync(services, tenantId, admin.ExternalId, slug, ownerEmail);
        var (_, ready) = await ActivateAsync(services, tenantId, started.Activate.ShouldNotBeNull().Message);

        return (tenantId, slug, ready.ShouldNotBeNull());
    }

    // Accepting starts outside any tenant: the invitation code names the tenant, the identity provider is asked for the user's
    // verified email addresses, and then the invitation is found by its secret in that tenant.
    public static async Task<Result<TenantSummary>> AcceptAsync(IServiceProvider services, string code, string userId)
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

    // The invitation code an accept link carries: `<tenantId>.<secret>`.
    public static string CodeOf(Uri link) =>
        Uri.UnescapeDataString(link.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("code=", StringComparison.Ordinal))["code=".Length..]);

    public static Guid TenantIdOf(string code) => Guid.Parse(code[..code.IndexOf('.', StringComparison.Ordinal)]);

    public static string SecretOf(string code) => code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..];

    public sealed record StartedOnboarding(
        Result<TenantDetails> Result,
        Insert<TenantOnboarding>? Onboarding,
        DeliveryMessage<ActivateTenant>? Activate,
        ActivationTimedOut? Timeout);
}
