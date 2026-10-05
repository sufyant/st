using Api.Messaging;
using Tenancy;

namespace Api.Persistence;

// The separate migration step (0020): `dotnet Api.dll migrate` applies every module's migrations and creates the message storage
// as the owner, over the direct connection, and exits. Starting the application never migrates.
internal static class MigrationStep
{
    public const string Command = "migrate";

    public static Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Migrations");

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException("ConnectionStrings:Migrations must name the owner role's direct connection.")
            : RunAsync(services, connectionString, cancellationToken);
    }

    public static async Task RunAsync(IServiceProvider services, string connectionString, CancellationToken cancellationToken)
    {
        foreach (var migrator in services.GetServices<IModuleMigrator>())
        {
            await migrator.MigrateAsync(services, connectionString, cancellationToken);
        }

        await MessageStorage.MigrateAsync(connectionString, cancellationToken);
    }
}
