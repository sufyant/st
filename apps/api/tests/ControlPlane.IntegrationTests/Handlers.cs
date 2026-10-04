using ControlPlane.Application.Invitations;
using ControlPlane.Application.Members;
using ControlPlane.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace ControlPlane.IntegrationTests;

// The module's handlers, called in their tenant with the services Wolverine would pass them.
internal static class Handlers
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static Task<Result<InvitationDetails>> InviteAsync(IServiceProvider services, Guid tenantId, string actorId, string email, Guid roleId) =>
        InTenant.RunAsync(services, tenantId, scope => CreateInvitationHandler.HandleAsync(
            new CreateInvitation(actorId, email, roleId),
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<IIdentityProvider>(),
            scope.GetRequiredService<IInvitationSender>(),
            scope.GetRequiredService<InvitationSettings>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));

    // Accepting starts outside any tenant: the token leads to the tenant, then the invitation is accepted in it.
    public static async Task<Result<InvitationAccepted>> AcceptAsync(IServiceProvider services, string token, string userId)
    {
        Guid? tenantId;
        await using (var scope = services.CreateAsyncScope())
        {
            tenantId = await scope.ServiceProvider.GetRequiredService<IInvitationDirectory>().FindTenantAsync(token, Cancellation);
        }

        return await InTenant.RunAsync(services, tenantId!.Value, scope => AcceptInvitationHandler.HandleAsync(
            new AcceptInvitation(token, userId),
            scope.GetRequiredService<ITenantCatalog>(),
            scope.GetRequiredService<IIdentityProvider>(),
            scope.GetRequiredService<TimeProvider>(),
            Cancellation));
    }

    public static Task<Result> ChangeMemberRoleAsync(IServiceProvider services, Guid tenantId, string actorId, Guid userId, Guid roleId) =>
        InTenant.RunAsync(services, tenantId, scope => ChangeMemberRoleHandler.HandleAsync(
            new ChangeMemberRole(actorId, userId, roleId), scope.GetRequiredService<ITenantCatalog>(), Cancellation));

    public static string TokenOf(Uri link) =>
        Uri.UnescapeDataString(link.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("token=", StringComparison.Ordinal))["token=".Length..]);
}
