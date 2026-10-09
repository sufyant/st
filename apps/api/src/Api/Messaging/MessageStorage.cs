using Api.Hosting;
using JasperFx;
using Npgsql;
using Tenancy;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace Api.Messaging;

// Wolverine keeps its envelopes in one shared schema, and its durability agent holds session-level advisory locks there. A handler's
// own envelopes are written by its module DbContext, in the transaction Wolverine began for the handler (W1). The migration step
// creates the schema as the owner; starting never does.
//
// The process roles (section 1, W5): a web host runs a command a request sends inline, and every message that command sends goes,
// through the outbox, to a PostgreSQL queue in the same schema; the web host listens to nothing and runs no durability agent. A worker
// sends every message to that queue too and listens to it, so whatever a handler sends on is handled by some worker. A host in the
// role all hands its messages to its own durable local queues.
internal static class MessageStorage
{
    public const string Schema = TenancyServiceCollectionExtensions.MessageSchema;

    public const string Queue = "messages";

    // How long an idle worker waits before it looks for new messages in the queue again.
    private static readonly TimeSpan QueuePollingInterval = TimeSpan.FromSeconds(1);

    public static void Configure(WolverineOptions options, NpgsqlDataSource dataSource, HostRole? role)
    {
        var storage = options.PersistMessagesWithPostgresql(dataSource, Schema).OverrideAutoCreateResources(AutoCreate.None);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        options.Policies.UseDurableLocalQueues();

        if (role is HostRole.Web or HostRole.Worker)
        {
            storage.EnableMessageTransport(transport => transport.TransportSchemaName(Schema));
            options.PublishAllMessages().ToPostgresqlQueue(Queue);
        }

        if (role is HostRole.Worker)
        {
            options.ListenToPostgresqlQueue(Queue).PollingInterval(QueuePollingInterval);
        }

        if (role is HostRole.Web)
        {
            options.Durability.DurabilityAgentEnabled = false;
        }
    }

    // Wolverine builds its store only inside a host. This one is never started: it creates or updates the schema as the owner, with the
    // configuration a worker uses, and lets the application role use the tables. The PostgreSQL transport adds its queue tables to the
    // store as a runtime starts; the step asks it to, as the start would.
    public static async Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(ownerConnectionString);
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddWolverine(options => Configure(options, dataSource, HostRole.Worker));
        var host = builder.Build();

        try
        {
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            foreach (var transport in runtime.Options.Transports.OfType<ITransportConfiguresRuntime>().ToArray())
            {
                await transport.ConfigureAsync(runtime);
            }

            await runtime.Storage.Admin.MigrateAsync(AutoCreate.CreateOrUpdate);

            await using var grant = dataSource.CreateCommand(
                $"""
                GRANT USAGE ON SCHEMA {Schema} TO {DatabaseRoles.Application};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Schema} TO {DatabaseRoles.Application};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {Schema} TO {DatabaseRoles.Application};
                """);
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await ((IAsyncDisposable)host).DisposeAsync();
        }
    }
}
