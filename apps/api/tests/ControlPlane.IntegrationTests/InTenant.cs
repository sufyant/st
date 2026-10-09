using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace ControlPlane.IntegrationTests;

// Runs a handler the way Wolverine does: the message carries the tenant, the module's DbContext begins the transaction, which
// declares that tenant (W2), and when the handler returns its changes are saved and committed, a failed Result included (W7). An
// exception leaves the transaction uncommitted, and it rolls back.
internal static class InTenant
{
    // What a handler returns: its Result, or the messages it sends, which are the caller's to deliver, as the outbox would.
    public static async Task<T> RunAsync<T>(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task<T>> handler)
    {
        await using var scope = services.CreateAsyncScope();
        var catalog = Catalog(scope, tenantId);
        await using var transaction = await catalog.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var returned = await handler(scope.ServiceProvider);

        await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return returned;
    }

    public static Task RunAsync(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task> handler) =>
        RunAsync(services, tenantId, async scope =>
        {
            await handler(scope);
            return true;
        });

    public static async Task<T> ReadAsync<T>(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = services.CreateAsyncScope();
        await using var transaction = await Catalog(scope, tenantId).Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        return await read(scope.ServiceProvider);
    }

    // The scope's message carries the tenant, the way Wolverine hands the message context to the DbContext it creates.
    public static CatalogDbContext Catalog(AsyncServiceScope scope, Guid tenantId)
    {
        scope.ServiceProvider.GetRequiredService<IMessageContext>().TenantId = tenantId.ToString();
        return scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    }
}
