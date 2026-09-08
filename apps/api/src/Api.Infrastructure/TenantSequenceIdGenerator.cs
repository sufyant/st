using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantSequenceIdGenerator(AdminDbContext dbContext)
{
    public async Task<string> NextAsync(
        string schemaName, string sequenceName, string prefix, CancellationToken cancellationToken = default)
    {
        SafePostgresIdentifier.EnsureSafe(schemaName, nameof(schemaName));
        SafePostgresIdentifier.EnsureSafe(sequenceName, nameof(sequenceName));

#pragma warning disable EF1002 // Value is validated via SafePostgresIdentifier.EnsureSafe before this call
        await dbContext.Database.ExecuteSqlRawAsync(
            $"CREATE SEQUENCE IF NOT EXISTS \"{schemaName}\".\"{sequenceName}\"", cancellationToken);
#pragma warning restore EF1002

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('\"{schemaName}\".\"{sequenceName}\"')";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var value = Convert.ToInt64(result);
            return $"{prefix}-{value:D6}";
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
