using Tenancy;

namespace Api.Persistence;

internal static class PersistenceExtensions
{
    private const string Section = "ConnectionStrings";

    public const string DatabaseConnection = "Database";

    public const string MessagingConnection = "Messaging";

    public static WebApplicationBuilder AddPersistence(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ConnectionStringOptions>()
            .BindConfiguration(Section)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Database), $"{Section}:{DatabaseConnection} must name the application role's connection.")
            .ValidateOnStart();

        // Both connections are checked on start (DatabaseAccountCheck), which runs before Wolverine starts. A host composed without the
        // setting, as for the migration step, registers no database; it does not start.
        builder.Services.AddHostedService<DatabaseAccountCheck>();
        if (builder.Configuration.GetConnectionString(DatabaseConnection) is { Length: > 0 } database)
        {
            builder.Services.AddTenancy(_ => database);
        }

        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return builder;
    }

    // Wolverine's message store holds session-level advisory locks, so it needs a connection of its own behind a pooler in transaction
    // mode; without one it uses the database connection. The key names the setting the connection came from.
    public static (string Key, string ConnectionString)? MessagingConnectionOf(IConfiguration configuration) =>
        configuration.GetConnectionString(MessagingConnection) is { Length: > 0 } messaging ? ($"{Section}:{MessagingConnection}", messaging)
        : configuration.GetConnectionString(DatabaseConnection) is { Length: > 0 } database ? ($"{Section}:{DatabaseConnection}", database)
        : null;

    public static string DatabaseKey => $"{Section}:{DatabaseConnection}";

    private sealed class ConnectionStringOptions
    {
        public string? Database { get; set; }
    }
}
