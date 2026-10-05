using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Tenancy;

namespace Notifications.IntegrationTests;

// Runs a handler the way the host's transaction policy does (0016): in one transaction with the tenant set at its start,
// committed only when the handler succeeds.
internal static class InTenant
{
    public static async Task<TResult> RunAsync<TResult>(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task<TResult>> handler)
        where TResult : Result
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        var result = await handler(scope.ServiceProvider);

        if (result.IsSuccess)
        {
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        return result;
    }

    // A message handler without a Result, committed unless it throws.
    public static async Task ProcessAsync(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task> handler)
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        await handler(scope.ServiceProvider);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }
}
