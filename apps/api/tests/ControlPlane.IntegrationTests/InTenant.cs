using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Tenancy;

namespace ControlPlane.IntegrationTests;

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

    public static async Task<T> ReadAsync<T>(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        return await read(scope.ServiceProvider);
    }
}
