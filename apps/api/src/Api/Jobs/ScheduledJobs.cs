using Api.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Npgsql;
using Tenancy;

namespace Api.Jobs;

// Hangfire runs the system-defined recurring jobs and the scanner of user-defined schedules, each once across all pods (0027).
// Its tables live in their own schema, created by the migration step as the owner (0020); starting never creates them. It works
// over the pooled connection: its locks are rows in its own tables, not session state (0019).
internal static class ScheduledJobs
{
    public const string Schema = "hangfire";

    // Jobs only send a message or run one statement, so two workers are enough, and one slow job never holds up the others.
    private const int WorkerCount = 2;

    private const string DashboardPath = "/hangfire";

    public static WebApplicationBuilder AddScheduledJobs(this WebApplicationBuilder builder)
    {
        // Like Wolverine's storage (ApiPipeline), Hangfire takes its connection while it is configured; the build writes the OpenAPI
        // document from a host without it, and that host runs no jobs.
        if (builder.Configuration.GetConnectionString(PersistenceExtensions.PooledConnection) is not { Length: > 0 })
        {
            return builder;
        }

        // The storage is the host's own service rather than Hangfire's static JobStorage.Current, so hosts in one process never
        // share it.
        builder.Services.AddSingleton<JobStorage>(provider =>
            new PostgreSqlStorage(new DataSourceConnectionFactory(provider.GetRequiredService<NpgsqlDataSource>()), StorageOptions(prepareSchema: false)));
        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings());
        builder.Services.AddHangfireServer(options => options.WorkerCount = WorkerCount);

        return builder;
    }

    // Each module schedules its system-defined jobs on every start; scheduling an existing job only updates it. It happens while the
    // host starts, after Wolverine has checked the database (0038), and not at all in a host without Hangfire.
    public static IServiceCollection ScheduleRecurringJobs(this IServiceCollection services, params Action<IRecurringJobManager>[] schedules) =>
        services.AddHostedService(provider => new RecurringJobsSchedule(provider, schedules));

    // The dashboard is open only in local development until the system admin console exists (0027, 0031).
    public static WebApplication UseJobsDashboard(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() && app.Services.GetService<JobStorage>() is not null)
        {
            app.UseHangfireDashboard(DashboardPath);
        }

        return app;
    }

    // Creates or updates Hangfire's schema as the owner and lets the application role use it, like the message storage (0020).
    public static async Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(ownerConnectionString);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            PostgreSqlObjectsInstaller.Install(connection, Schema);
        }

        await using var grant = dataSource.CreateCommand(
            $"""
            GRANT USAGE ON SCHEMA {Schema} TO {DatabaseRoles.Application};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Schema} TO {DatabaseRoles.Application};
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {Schema} TO {DatabaseRoles.Application};
            """);
        await grant.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PostgreSqlStorageOptions StorageOptions(bool prepareSchema) => new()
    {
        SchemaName = Schema,
        PrepareSchemaIfNecessary = prepareSchema,
    };

    private sealed class RecurringJobsSchedule(IServiceProvider services, Action<IRecurringJobManager>[] schedules) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (services.GetService<IRecurringJobManager>() is { } jobs)
            {
                foreach (var schedule in schedules)
                {
                    schedule(jobs);
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Hangfire opens and closes the connections it is given; they come from the application's pooled data source.
    private sealed class DataSourceConnectionFactory(NpgsqlDataSource dataSource) : IConnectionFactory
    {
        public NpgsqlConnection GetOrCreateConnection() => dataSource.CreateConnection();
    }
}
