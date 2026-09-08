using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed partial class TenantSequenceIdGenerator(AdminDbContext dbContext)
{
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeIdentifierPattern();

    public async Task<string> NextAsync(
        string schemaName, string sequenceName, string prefix, CancellationToken cancellationToken = default)
    {
        EnsureSafeIdentifier(schemaName, nameof(schemaName));
        EnsureSafeIdentifier(sequenceName, nameof(sequenceName));

        await dbContext.Database.ExecuteSqlRawAsync(
            $"CREATE SEQUENCE IF NOT EXISTS \"{schemaName}\".\"{sequenceName}\"", cancellationToken);

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

    private static void EnsureSafeIdentifier(string value, string paramName)
    {
        if (!SafeIdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe Postgres identifier.", paramName);
        }
    }
}
