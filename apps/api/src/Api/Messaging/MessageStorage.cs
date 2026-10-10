using System.Reflection;
using Api.Hosting;
using Audit.Api;
using JasperFx;
using Npgsql;
using Tenancy;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.Runtime;
using Wolverine.Runtime.Agents;
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
//
// Each handler of a message runs in its own transaction (W4). A worker hands a message with several handlers on to a durable local
// queue for each, but Wolverine runs a saga at the endpoint its message arrives on and then hands the message on to no other handler.
// So a handler that shares its message with a saga has a PostgreSQL queue of its own in the roles web and worker: every host sends
// the message there too, and only a worker listens to it, running that handler alone.
internal static class MessageStorage
{
    public const string Schema = TenancyServiceCollectionExtensions.MessageSchema;

    public const string Queue = "messages";

    // How long an idle worker waits before it looks for new messages in the queue again.
    private static readonly TimeSpan QueuePollingInterval = TimeSpan.FromSeconds(1);

    private static readonly string[] HandlerMethodNames = ["Handle", "HandleAsync", "Consume", "ConsumeAsync"];

    // The handlers that share their message with a saga (W5).
    private static readonly Type[] HandlersBesideASaga = [.. AuditModule.HandlersBesideASaga];

    public static void Configure(WolverineOptions options, NpgsqlDataSource dataSource, HostRole? role)
    {
        var storage = options.PersistMessagesWithPostgresql(dataSource, Schema).OverrideAutoCreateResources(AutoCreate.None);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        options.Policies.UseDurableLocalQueues();

        if (role is HostRole.Web or HostRole.Worker)
        {
            storage.EnableMessageTransport(transport => transport.TransportSchemaName(Schema));
            options.PublishAllMessages().ToPostgresqlQueue(Queue);
            foreach (var handler in HandlersBesideASaga)
            {
                options.PublishMessage(MessageOf(handler)).ToPostgresqlQueue(QueueOf(handler));
            }
        }

        if (role is HostRole.Worker)
        {
            options.ListenToPostgresqlQueue(Queue).PollingInterval(QueuePollingInterval);
            foreach (var handler in HandlersBesideASaga)
            {
                options.ListenToPostgresqlQueue(QueueOf(handler)).PollingInterval(QueuePollingInterval).AddStickyHandler(handler);
            }
        }

        if (role is HostRole.Web)
        {
            options.Durability.DurabilityAgentEnabled = false;

            // A web host can win the leader election, and the leader assigns only the agent families it knows. Wolverine adds the
            // durability agents' family only on a node that may run them, so a web leader would assign them to no node, and nothing
            // would recover a dead worker's messages. The family gives its agents only to nodes that may run them: the workers.
            options.Services.AddSingleton<IAgentFamily>(services => services.GetRequiredService<IWolverineRuntime>().Stores);
        }
    }

    private static string QueueOf(Type handler) => handler.Name.ToLowerInvariant();

    // The message a handler class handles: the first parameter of its handler method, by Wolverine's naming.
    private static Type MessageOf(Type handler) =>
        handler.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => HandlerMethodNames.Contains(method.Name))
            .GetParameters()[0].ParameterType;

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
