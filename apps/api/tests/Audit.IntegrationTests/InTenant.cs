using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;

namespace Audit.IntegrationTests;

// Runs work the way the host's transaction policy does (0016): in one transaction with the tenant set at its start, committed
// unless it throws.
internal static class InTenant
{
    public static async Task RunAsync(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task> work)
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        await work(scope.ServiceProvider);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    // Runs SQL as the application role inside the tenant's transaction, so row level security applies.
    public static async Task<T> SqlAsync<T>(IServiceProvider services, Guid tenantId, Func<NpgsqlCommand, Task<T>> run, string sql)
    {
        await using var scope = services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction.Current);

        return await run(command);
    }
}
