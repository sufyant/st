using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class PostgresTenantSchemaProvisioner(NpgsqlDataSource dataSource)
{
    public async Task CreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        var schemaName = tenantId.ToString("N");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
