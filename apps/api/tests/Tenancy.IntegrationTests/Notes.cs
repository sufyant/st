using Microsoft.Extensions.DependencyInjection;

namespace Tenancy.IntegrationTests;

// Runs work the way the host does: in a scope, on the scope's connection, inside a tenant transaction when a tenant is given.
internal sealed class Notes(string connectionString) : IAsyncDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddTenancy(_ => connectionString)
        .AddModuleDbContext<NotesDbContext>(NotesDbContext.Schema)
        .BuildServiceProvider();

    public Task InTenantAsync(Guid tenantId, Func<NotesDbContext, Task> work) =>
        InTenantAsync(tenantId, async notes =>
        {
            await work(notes);
            return true;
        });

    public async Task<T> InTenantAsync<T>(Guid tenantId, Func<NotesDbContext, Task<T>> work)
    {
        await using var scope = _services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);

        var result = await work(scope.ServiceProvider.GetRequiredService<NotesDbContext>());

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return result;
    }

    public async Task<T> WithoutTenantAsync<T>(Func<NotesDbContext, Task<T>> work)
    {
        await using var scope = _services.CreateAsyncScope();

        return await work(scope.ServiceProvider.GetRequiredService<NotesDbContext>());
    }

    public static async Task<TenantTransaction> BeginUncommittedAsync(AsyncServiceScope scope, Guid tenantId)
    {
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);
        return transaction;
    }

    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

// The tests share one database, so every test works in tenants of its own. The values never matter, only that they differ.
internal static class Tenants
{
    public static Guid New() => Guid.NewGuid();
}
