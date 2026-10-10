using Api.Messaging;
using Tenancy;

namespace Api.Persistence;

// The separate migration step: `dotnet Api.dll migrate` applies every module's migrations and creates the message storage
// as the owner, and exits. Starting the application never migrates. It logs a line for each module and one when it finishes.
internal static partial class MigrationStep
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
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrationStep));
        var migrators = services.GetServices<IModuleMigrator>().ToList();
        foreach (var migrator in migrators)
        {
            var applied = await migrator.MigrateAsync(services, connectionString, cancellationToken);
            LogModuleMigrated(logger, migrator.Schema, applied.Count);
        }

        await MessageStorage.MigrateAsync(connectionString, cancellationToken);
        var schemas = string.Join(", ", migrators.Select(migrator => migrator.Schema));
        LogFinished(logger, schemas, MessageStorage.Schema);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated the {Schema} schema: {Applied} migrations applied")]
    private static partial void LogModuleMigrated(ILogger logger, string schema, int applied);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration finished: the schemas {Schemas} and the message storage in {MessageSchema} are up to date")]
    private static partial void LogFinished(ILogger logger, string schemas, string messageSchema);
}
