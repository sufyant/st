using System.Collections.Concurrent;
using System.Diagnostics;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Tenancy;

namespace Api.IntegrationTests;

// Section 7: the trace id of a request is the trace id in the handler of every message the request caused, and section 8: in the log,
// too. Here the request reaches a web host and every later step runs in a worker, so the trace crosses the PostgreSQL queue.
public sealed class TraceIdTests(Database database)
{
    // Every handler of the onboarding, as section 6 lists them, by the message it handles.
    private static readonly string[] OnboardingHandlers =
    [
        typeof(StartTenantOnboarding).FullName!,
        typeof(RegisterOwnerWithIdentityProvider).FullName!,
        typeof(OwnerRegistered).FullName!,
        typeof(ActivateTenant).FullName!,
        typeof(TenantActivated).FullName!,
        typeof(OwnerInvitationReady).FullName!,
        typeof(InvitationEmailSent).FullName!,
    ];

    [Fact]
    public async Task OnboardTenant_RequestCarriesATraceParent_EveryHandlerAndItsLogCarryItsTraceId()
    {
        using var spans = new HandlerSpans();
        var logs = new LogRecords();
        var name = await database.CreateMigratedDatabaseAsync();
        await using var web = Host(name, "web", logs);
        await using var worker = Host(name, "worker", logs);
        _ = worker.Services;
        var admin = web.CreateClient(await TwoWorkers.AddSystemAdminAsync(database, name), secondFactor: true);
        var traceId = ActivityTraceId.CreateRandom();
        admin.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = "acme", ownerEmail = "owner@acme.test" });
        var tenantId = await TwoWorkers.IdOfAsync(created);
        await Waiting.UntilAsync(() => spans.OfTenant(tenantId).Any(span => span.MessageType == typeof(InvitationEmailSent).FullName));
        var handlers = spans.OfTenant(tenantId);

        handlers.Select(span => span.MessageType).Distinct().ShouldBe(OnboardingHandlers, ignoreOrder: true);
        handlers.Select(span => span.TraceId).Distinct().ShouldBe([traceId]);
        logs.TraceIdsOfEmailsTo("owner@acme.test").ShouldBe([traceId]);
    }

    // The worker writes the invitation email to its log: in Development without Resend, that is the email channel.
    private ApiFactory Host(string databaseName, string role, LogRecords logs) => new(
        database.ConnectionStringFor(DatabaseRoles.Application, databaseName),
        fakeEmailChannel: false,
        settings: new Dictionary<string, string?> { ["Host:Role"] = role },
        configureServices: services => services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddProcessor(logs)));

    // The handler spans Wolverine starts in any host of this process. It names a handler's span after the message type it handles.
    private sealed class HandlerSpans : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _stopped = new();
        private readonly ActivityListener _listener;

        public HandlerSpans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Wolverine",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<(string MessageType, ActivityTraceId TraceId)> OfTenant(Guid tenantId) =>
        [
            .. _stopped
                .Where(span => span.GetTagItem("tenant.id") as string == tenantId.ToString())
                .Where(span => span.OperationName == span.GetTagItem("messaging.message_type") as string)
                .Select(span => (span.OperationName, span.TraceId)),
        ];

        public void Dispose() => _listener.Dispose();
    }

    // The log records as the application hands them to OpenTelemetry, which exports them.
    private sealed class LogRecords : BaseProcessor<LogRecord>
    {
        private readonly ConcurrentQueue<(string? Category, ActivityTraceId TraceId, string? To)> _records = new();

        public override void OnEnd(LogRecord data) => _records.Enqueue((
            data.CategoryName,
            data.TraceId,
            data.Attributes?.FirstOrDefault(attribute => attribute.Key == "To").Value as string));

        public IEnumerable<ActivityTraceId> TraceIdsOfEmailsTo(string to) =>
            _records.Where(record => record.Category?.EndsWith(".LogEmailChannel", StringComparison.Ordinal) == true && record.To == to)
                .Select(record => record.TraceId);
    }
}
