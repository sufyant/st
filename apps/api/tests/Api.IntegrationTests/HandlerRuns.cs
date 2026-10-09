using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Api.IntegrationTests;

// A test-only probe: which host ran each handler, by the type of the message it handled. Hosts that share one record of runs show
// where every message of a flow was handled.
public sealed class HandlerRuns
{
    private readonly ConcurrentQueue<(string Host, Type Message)> _runs = new();
    private readonly ConcurrentDictionary<Type, TaskCompletionSource> _handled = new();

    public IEnumerable<Type> In(string host) => _runs.Where(run => run.Host == host).Select(run => run.Message);

    // Completes when a handler of the message has run in any host, or fails after the time a test may wait.
    public Task HandledAsync<TMessage>() =>
        Signal(typeof(TMessage)).Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

    internal void Record(string host, Type message)
    {
        _runs.Enqueue((host, message));
        Signal(message).TrySetResult();
    }

    private TaskCompletionSource Signal(Type message) =>
        _handled.GetOrAdd(message, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

// Puts a host's handlers on the record, each after it handled its message.
public sealed class HandlerRunsOfHost(string host, HandlerRuns runs)
{
    public static Action<IServiceCollection> Register(string host, HandlerRuns runs) => services =>
    {
        services.AddSingleton(new HandlerRunsOfHost(host, runs));
        services.ConfigureWolverine(options => options.Policies.AddMiddleware(typeof(RecordHandlerRun)));
    };

    internal void Record(Envelope envelope) => runs.Record(host, envelope.Message!.GetType());
}

// Wolverine middleware, so public.
public static class RecordHandlerRun
{
    public static void After(Envelope envelope, HandlerRunsOfHost runs) => runs.Record(envelope);
}
