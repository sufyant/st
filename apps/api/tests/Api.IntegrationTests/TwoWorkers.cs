using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notifications.Application;
using Notifications.Application.Ports;
using Npgsql;
using Tenancy;
using Wolverine;
using Wolverine.Runtime;

namespace Api.IntegrationTests;

// One web host and two workers on one database of their own, as the roles run in a deployment (section 1, W5). Each host knows its
// own name, and all three share the test's probes: the record of handler runs, the handlings table and the hold on a first caller.
internal sealed class TwoWorkers : IAsyncDisposable
{
    public const string First = "worker-1";
    public const string Second = "worker-2";

    private readonly Database _database;
    private readonly Dictionary<string, ApiFactory> _workers;

    private TwoWorkers(Database database, string databaseName)
    {
        _database = database;
        DatabaseName = databaseName;
        Handlings = new Handlings(database.ConnectionStringFor(DatabaseRoles.Application, databaseName));
        Web = Host("web", "web");
        _workers = new() { [First] = Host(First, "worker"), [Second] = Host(Second, "worker") };
    }

    public string DatabaseName { get; }

    public ApiFactory Web { get; }

    public HandlerRuns Runs { get; } = new();

    public Handlings Handlings { get; }

    public HoldFirstCaller Hold { get; } = new();

    public FakeEmailChannel Email { get; } = new();

    public static async Task<TwoWorkers> StartAsync(Database database)
    {
        var name = await database.CreateMigratedDatabaseAsync();
        await database.ScalarAsSuperuserAsync<object>(Handlings.CreateTable, name);
        var workers = new TwoWorkers(database, name);

        // The web host leads the cluster: the case in which the workers' durability agents are assigned by a node that runs none.
        _ = workers.Web.Services;
        await Waiting.UntilAsync(async () => await database.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM wolverine.wolverine_node_assignments WHERE id = 'wolverine://leader/'", name) == 1);
        foreach (var worker in workers._workers.Values)
        {
            _ = worker.Services;
        }

        return workers;
    }

    // The node a worker registered as in Wolverine's node table.
    public Guid NodeOf(string worker) => _workers[worker].Services.GetRequiredService<IWolverineRuntime>().Options.UniqueNodeId;

    // The node that runs the durability agent of the message store, once the leader has assigned it.
    public async Task<Guid> DurabilityAgentNodeAsync()
    {
        await Waiting.UntilAsync(async () => await _database.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM wolverine.wolverine_node_assignments WHERE id LIKE 'wolverinedb://%'", DatabaseName) == 1);
        return (await _database.ScalarAsSuperuserAsync<Guid>(
            "SELECT node_id FROM wolverine.wolverine_node_assignments WHERE id LIKE 'wolverinedb://%'", DatabaseName));
    }

    // The worker that is not the given one.
    public string Survivor(string worker) => _workers.Keys.Single(name => name != worker);

    public async Task SendFromWebAsync(IEnumerable<object> messages)
    {
        await using var scope = Web.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        foreach (var message in messages)
        {
            await bus.SendAsync(message);
        }
    }

    // Kills the worker as closely as Wolverine allows inside one process: its quick stop mode skips draining, skips releasing the node's
    // ownership of its messages and stops its heartbeat. Disposing the host then stops its listeners without waiting for the handler in
    // flight, drops the receiver that would mark that handler's message as handled, and closes its connections, so its advisory locks
    // go as a dead process's would. The handler in flight never returns.
    public async Task CrashAsync(string worker)
    {
        ((WolverineRuntime)_workers[worker].Services.GetRequiredService<IWolverineRuntime>()).StopMode = StopMode.Quick;
        await _workers[worker].DisposeAsync();
    }

    // Waits until the inbox shows the given number of messages of the type as handled, so no host will run any of them again.
    public Task WaitForHandledAsync<TMessage>(int count) => Waiting.UntilAsync(async () => await _database.ScalarAsSuperuserAsync<long>(
        $"SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE status = 'Handled' AND message_type = '{typeof(TMessage).FullName}'",
        DatabaseName) == count);

    public Task WaitForOnboardingStateAsync(Guid tenantId, string state) =>
        Waiting.UntilAsync(async () => await OnboardingStateAsync(tenantId) == state);

    public Task<string?> OnboardingStateAsync(Guid tenantId) =>
        _database.ScalarAsSuperuserAsync<string>($"SELECT state FROM catalog.tenant_onboardings WHERE id = '{tenantId}'", DatabaseName);

    public Task<long> DeadLettersAsync() =>
        _database.ScalarAsSuperuserAsync<long>("SELECT count(*) FROM wolverine.wolverine_dead_letters", DatabaseName);

    public Task<string> AddSystemAdminAsync() => AddSystemAdminAsync(_database, DatabaseName);

    // A system admin in the database's staff list, by the user's external id.
    public static async Task<string> AddSystemAdminAsync(Database database, string databaseName)
    {
        var externalId = $"user_{Guid.NewGuid():N}";
        await database.ScalarAsSuperuserAsync<object>(
            $"""
            INSERT INTO catalog.users (id, external_id) VALUES ('{Guid.NewGuid()}', '{externalId}');
            INSERT INTO catalog.system_admins (user_id, granted_at) SELECT id, now() FROM catalog.users WHERE external_id = '{externalId}';
            """,
            databaseName);

        return externalId;
    }

    public static async Task<Guid> IdOfAsync(HttpResponseMessage created) =>
        (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers.Values)
        {
            await worker.DisposeAsync();
        }

        await Web.DisposeAsync();
    }

    private ApiFactory Host(string name, string role) => new(
        _database.ConnectionStringFor(DatabaseRoles.Application, DatabaseName),
        settings: new Dictionary<string, string?> { ["Host:Role"] = role },
        configureServices: services =>
        {
            HandlerRunsOfHost.Register(name, Runs)(services);
            services.AddSingleton(new HostName(name));
            services.AddSingleton(Handlings);
            services.AddSingleton(Hold);
            services.Replace(ServiceDescriptor.Singleton<IEmailChannel>(new EmailChannelOfHost(name, Hold, Email)));
            services.ConfigureWolverine(options =>
            {
                options.Discovery.IncludeType(typeof(RecordHandlingHandler));
                options.Discovery.IncludeType(typeof(RecordHandlingThenHoldHandler));
                ShortenNodeTimings(options.Durability);
            });
        });

    // Wolverine's defaults notice a dead node after a minute and look for messages without a live owner every five seconds. A test
    // waits for both, so they are shortened here; the order of events stays the same.
    private static void ShortenNodeTimings(DurabilitySettings durability)
    {
        // How often a node writes its heartbeat and looks for nodes whose heartbeat is too old (10 s), and when it first does (3 s).
        durability.HealthCheckPollingTime = TimeSpan.FromSeconds(1);
        durability.FirstHealthCheckExecution = TimeSpan.Zero;

        // How old a heartbeat may be before its node counts as dead (1 min). Another node removes a dead node, and releases the
        // messages it owned, after seeing it dead on two health checks in a row.
        durability.StaleNodeTimeout = TimeSpan.FromSeconds(3);

        // How often the durability agent takes over messages that no live node owns (5 s), and when it first does (0.5 to 5 s).
        durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
        durability.ScheduledJobFirstExecution = TimeSpan.Zero;
        durability.OrphanedMessageSweepPollingTime = TimeSpan.FromSeconds(1);
    }
}

// The name of the host a handler runs in. Wolverine passes it to a handler, so it is public.
public sealed record HostName(string Name);

// A table that records each handling of a test message under a unique key on the message's id, and the host of each run that
// started, whether it finished or not.
public sealed class Handlings(string connectionString)
{
    public const string Table = "public.message_handlings";

    public static readonly string CreateTable =
        $"""
        CREATE TABLE {Table} (message_id uuid PRIMARY KEY, worker text NOT NULL);
        GRANT SELECT, INSERT ON {Table} TO {DatabaseRoles.Application};
        """;

    private readonly ConcurrentQueue<string> _runs = new();

    public IReadOnlyCollection<string> Runs => _runs;

    // A second handling of the same message fails on the key instead of passing unseen.
    public Task RecordAsync(Guid messageId, string worker, CancellationToken cancellationToken) =>
        InsertAsync($"INSERT INTO {Table} VALUES (@id, @worker)", messageId, worker, cancellationToken);

    // A second handling of the same message has no effect (O4).
    public Task RecordOnceAsync(Guid messageId, string worker, CancellationToken cancellationToken) =>
        InsertAsync($"INSERT INTO {Table} VALUES (@id, @worker) ON CONFLICT (message_id) DO NOTHING", messageId, worker, cancellationToken);

    public async Task<List<(Guid MessageId, string Worker)>> ReadAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT message_id, worker FROM {Table}", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        List<(Guid, string)> rows = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        return rows;
    }

    private async Task InsertAsync(string sql, Guid messageId, string worker, CancellationToken cancellationToken)
    {
        _runs.Enqueue(worker);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("worker", worker);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

// The first caller, in whichever host, is held for good, as if its host died at that moment. Every later caller passes.
public sealed class HoldFirstCaller
{
    private readonly TaskCompletionSource<string> _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _callers;

    // The host of the first caller, once it is held.
    public Task<string> HeldInAsync() => _held.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

    public Task PassAsync(string host)
    {
        if (Interlocked.Increment(ref _callers) > 1)
        {
            return Task.CompletedTask;
        }

        _held.SetResult(host);
        return new TaskCompletionSource().Task;
    }
}

// Each host's email channel: the first send is held, the others go to the email service all hosts share.
internal sealed class EmailChannelOfHost(string host, HoldFirstCaller hold, FakeEmailChannel email) : IEmailChannel
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        await hold.PassAsync(host);
        await email.SendAsync(message, cancellationToken);
    }
}

// Test messages for the workers. Wolverine discovers only public handlers and messages.
public sealed record RecordHandling(Guid Id);

public static class RecordHandlingHandler
{
    public static Task Handle(RecordHandling message, Handlings handlings, HostName host, CancellationToken cancellationToken) =>
        handlings.RecordAsync(message.Id, host.Name, cancellationToken);
}

// Records its handling, then its first run is held: the worker dies after the effect and before the message is marked as handled.
public sealed record RecordHandlingThenHold(Guid Id);

public static class RecordHandlingThenHoldHandler
{
    public static async Task Handle(
        RecordHandlingThenHold message, Handlings handlings, HoldFirstCaller hold, HostName host, CancellationToken cancellationToken)
    {
        await handlings.RecordOnceAsync(message.Id, host.Name, cancellationToken);
        await hold.PassAsync(host.Name);
    }
}
