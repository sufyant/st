using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Tenancy.IntegrationTests;

// Runs work the way a Wolverine handler does: in a scope whose message carries the tenant, inside the transaction the DbContext
// begins, which declares that tenant. Wolverine's own test double stands in for the message context; only its tenant is read.
internal sealed class Notes(string connectionString) : IAsyncDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddTenancy(_ => connectionString)
        .AddDbContext<NotesDbContext>((provider, options) => options.UseModuleDatabase(provider, NotesDbContext.Schema))
        .AddScoped<IMessageContext>(_ => new TestMessageContext())
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
        await using var transaction = await BeginUncommittedAsync(scope, tenantId);

        var result = await work(scope.ServiceProvider.GetRequiredService<NotesDbContext>());

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return result;
    }

    public async Task<T> WithoutTenantAsync<T>(Func<NotesDbContext, Task<T>> work)
    {
        await using var scope = _services.CreateAsyncScope();

        return await work(scope.ServiceProvider.GetRequiredService<NotesDbContext>());
    }

    public static Task<IDbContextTransaction> BeginUncommittedAsync(AsyncServiceScope scope, Guid tenantId)
    {
        scope.ServiceProvider.GetRequiredService<IMessageContext>().TenantId = tenantId.ToString();
        return scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
    }

    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

// The tests share one database, so every test works in tenants of its own. The values never matter, only that they differ.
internal static class Tenants
{
    public static Guid New() => Guid.NewGuid();
}
