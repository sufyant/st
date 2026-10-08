using Npgsql;

namespace Tenancy;

/// <summary>
/// The one connection and transaction of a request or message. Every module DbContext in the scope runs on this connection,
/// and a tenant-scoped transaction sets the tenant at its start, local to the transaction so a pooled connection never carries
/// it to the next user.
/// </summary>
public sealed class TenantTransaction(NpgsqlDataSource dataSource, TenantContext tenant) : IAsyncDisposable, IDisposable
{
    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public NpgsqlTransaction? Current { get; private set; }

    public async Task BeginAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (Current is not null)
        {
            throw new InvalidOperationException("A tenant transaction is already in progress.");
        }

        tenant.Set(tenantId);

        if (Connection.State != System.Data.ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }

        Current = await Connection.BeginTransactionAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"SELECT set_config('{TenantColumn.Setting}', @tenant, true)", Connection, Current);
        command.Parameters.AddWithValue("tenant", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = Current ?? throw new InvalidOperationException("No tenant transaction is in progress.");

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        Current = null;
    }

    // A transaction that was never committed is rolled back when it is disposed.
    public async ValueTask DisposeAsync()
    {
        if (Current is not null)
        {
            await Current.DisposeAsync();
        }

        await Connection.DisposeAsync();
    }

    // Wolverine's generated handlers dispose their scope synchronously.
    public void Dispose()
    {
        Current?.Dispose();
        Connection.Dispose();
    }
}
