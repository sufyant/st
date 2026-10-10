using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;
using Wolverine.Transports.Local;

namespace Architecture.Tests;

// W4, W5: in the roles web and worker, every handler of every message type is reached in a worker. A message reaches the handlers
// Wolverine runs at the endpoint it arrives on: at an endpoint a handler is stuck to, that handler; at another endpoint, the
// message type's handlers that are stuck nowhere. When every handler is stuck to a local queue, Wolverine hands a message that
// arrives from outside on to each of those queues. Read from Wolverine's own handler graph and routing in each role, against the
// endpoints a worker listens to.
public sealed class HandlerReachTests : IAsyncLifetime
{
    private readonly ComposedApplication _worker = new("worker");
    private readonly ComposedApplication _web = new("web");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _web.DisposeAsync();
    }

    [Theory]
    [InlineData("web")]
    [InlineData("worker")]
    public void RouteEveryMessage_FromEachRole_ReachesEveryHandlerInAWorker(string sender)
    {
        var worker = _worker.Services.GetRequiredService<IWolverineRuntime>();
        var sending = (sender == "web" ? _web : _worker).Services.GetRequiredService<IWolverineRuntime>();
        var listened = worker.Options.Transports.AllEndpoints().Where(endpoint => endpoint.IsListener).Select(endpoint => endpoint.Uri).ToHashSet();
        var graph = _worker.Services.GetRequiredService<HandlerGraph>();

        var unreached = graph.Chains
            .Where(chain => chain.MessageType.Assembly.GetName().Name is { } name && !name.StartsWith("Wolverine", StringComparison.Ordinal))
            .SelectMany(chain => Unreached(chain, sending.RoutingFor(chain.MessageType).Routes.Select(route => route.Describe().Endpoint).ToHashSet(), listened));

        unreached.ShouldBeEmpty();
    }

    // The handlers of the message type that no route takes to an endpoint a worker listens to, as "message -> handler".
    private static IEnumerable<string> Unreached(HandlerChain chain, HashSet<Uri> routes, HashSet<Uri> listened)
    {
        var stuck = chain.ByEndpoint.SelectMany(sticky => sticky.Endpoints).Select(endpoint => endpoint.Uri).ToHashSet();
        var arrivals = routes.Where(listened.Contains).ToArray();
        var fromOutside = arrivals.Any(uri => !stuck.Contains(uri));
        var handedOn = chain.Handlers.Count == 0 && fromOutside && chain.ByEndpoint.All(sticky => sticky.Endpoints.All(endpoint => endpoint is LocalQueue));

        var unstuckReached = chain.Handlers.Count == 0 || fromOutside;
        var stickyUnreached = chain.ByEndpoint.Where(sticky => !handedOn && !sticky.Endpoints.Any(endpoint => arrivals.Contains(endpoint.Uri)));

        return (unstuckReached ? [] : chain.Handlers.Select(call => call.HandlerType))
            .Concat(stickyUnreached.SelectMany(sticky => sticky.Handlers.Select(call => call.HandlerType)))
            .Distinct()
            .Select(handler => $"{chain.MessageType.Name} -> {handler.Name}");
    }
}
