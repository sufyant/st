using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime.Handlers;

namespace Architecture.Tests;

// W5: a worker hands an event with several handlers on to durable local queues, one for each handler (W4), and a handler that
// shares its event with a saga runs from a PostgreSQL queue of its own. A retry that Wolverine schedules for such a handler waits
// among the shared store's scheduled inbox rows, which every node polls, a web host included, so the handler could run in a web
// host. A retry inline, where the handler runs, stays in the worker. Read from Wolverine's own handler graph of the application
// composed as a worker, with the failure policies every chain has and the ones it has of its own.
public sealed class ScheduledRetryTests : IAsyncLifetime
{
    private const string WhyNot =
        "A scheduled retry of a message with several handlers waits among the scheduled inbox rows every node polls, so the handler "
        + "could run in a web host (W5). Retry it inline, or give the message one handler.";

    private readonly ComposedApplication _application = new("worker");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();

    [Fact]
    public void ReadTheHandlerGraph_EveryMessageWithSeveralHandlers_SchedulesNoRetry()
    {
        var graph = _application.Services.GetRequiredService<HandlerGraph>();
        HandlerChain[] chains = [.. graph.Chains.Concat(graph.Chains.SelectMany(chain => chain.ByEndpoint)).Where(chain => chain.Handlers.Count > 0)];
        var severalHandlers = chains
            .GroupBy(chain => chain.MessageType)
            .Where(message => message.SelectMany(chain => chain.Handlers).Select(call => call.HandlerType).Distinct().Count() > 1)
            .ToList();

        var scheduling = severalHandlers.SelectMany(message => message).Where(chain => SchedulesARetry(chain, graph)).Select(Describe);

        severalHandlers.Select(message => message.Key).ShouldContain(typeof(TenantActivated));
        scheduling.ShouldBeEmpty(WhyNot);
    }

    // Wolverine describes each attempt of a failure rule, and the attempt it repeats after the last, by what it does then.
    private static bool SchedulesARetry(HandlerChain chain, HandlerGraph graph) =>
        chain.Failures.Concat(graph.Failures).Any(rule => rule.ToString().Contains("Schedule Retry", StringComparison.Ordinal));

    private static string Describe(HandlerChain chain) =>
        $"{chain.MessageType.Name} -> {string.Join(", ", chain.Handlers.Select(call => call.HandlerType.Name))}: "
        + string.Join("; ", chain.Failures.Select(rule => rule.ToString()));
}
