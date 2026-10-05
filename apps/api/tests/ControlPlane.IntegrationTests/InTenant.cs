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

    // A handler that returns its Result with the message it sends; the message is the caller's to deliver, as the outbox would.
    public static async Task<(TResult Result, TMessage? Message)> RunAsync<TResult, TMessage>(
        IServiceProvider services,
        Guid tenantId,
        Func<IServiceProvider, Task<(TResult, TMessage?)>> handler)
        where TResult : Result
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        var (result, message) = await handler(scope.ServiceProvider);

        if (result.IsSuccess)
        {
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        return (result, message);
    }

    // A message handler without a Result, committed unless it throws; what it returns are the messages it sends.
    public static async Task<T> ProcessAsync<T>(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task<T>> handler)
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        var sent = await handler(scope.ServiceProvider);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return sent;
    }

    public static Task ProcessAsync(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task> handler) =>
        ProcessAsync(services, tenantId, async scope =>
        {
            await handler(scope);
            return true;
        });

    public static async Task<T> ReadAsync<T>(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        return await read(scope.ServiceProvider);
    }
}
