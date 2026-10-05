using Notifications.Application.Ports;
using Npgsql;

namespace Notifications.Infrastructure;

// Calls the SECURITY DEFINER lookup the migration creates (0017), outside any tenant: it sees every tenant's due notifications but
// returns only their tenant and id.
internal sealed class DueNotificationScan(NpgsqlDataSource dataSource) : IDueNotificationScan
{
    public const string Function = "due_scheduled_notifications";

    public async Task<IReadOnlyList<DueNotification>> FindDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT tenant_id, id FROM {NotificationsDbContext.Schema}.{Function}($1)");
        command.Parameters.Add(new NpgsqlParameter { Value = now });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var due = new List<DueNotification>();
        while (await reader.ReadAsync(cancellationToken))
        {
            due.Add(new DueNotification(reader.GetGuid(0), reader.GetGuid(1)));
        }

        return due;
    }
}
