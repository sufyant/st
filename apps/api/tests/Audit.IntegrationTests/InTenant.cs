using Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;

namespace Audit.IntegrationTests;

// Runs work the way Wolverine runs a handler: the message carries the tenant, the module's DbContext begins the transaction, which
// declares that tenant, and the work is saved and committed unless it throws.
internal static class InTenant
{
    public static async Task RunAsync(IServiceProvider services, Guid tenantId, Func<IServiceProvider, Task> work)
    {
        await using var scope = services.CreateAsyncScope();
        var audit = Audit(scope, tenantId);
        await using var transaction = await audit.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await work(scope.ServiceProvider);

        await audit.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    // Runs SQL as the application role inside the tenant's transaction, so row level security applies.
    public static async Task<T> SqlAsync<T>(IServiceProvider services, Guid tenantId, Func<NpgsqlCommand, Task<T>> run, string sql)
    {
        await using var scope = services.CreateAsyncScope();
        var audit = Audit(scope, tenantId);
        await using var transaction = await audit.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            sql, (NpgsqlConnection)audit.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());

        return await run(command);
    }

    private static AuditDbContext Audit(AsyncServiceScope scope, Guid tenantId)
    {
        scope.ServiceProvider.GetRequiredService<IMessageContext>().TenantId = tenantId.ToString();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }
}
