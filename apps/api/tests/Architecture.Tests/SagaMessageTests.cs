using ControlPlane.Application.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Wolverine;
using Wolverine.Runtime.Handlers;

namespace Architecture.Tests;

// S13: a saga shares no message with another handler. It hears its steps through replies and faults sent to it, not through events
// meant for other modules. Read from Wolverine's own handler graph of the application composed as a worker: the handlers of a
// message type are its chain's own and those Wolverine separated onto endpoints of their own (W4).
public sealed class SagaMessageTests : IAsyncLifetime
{
    private const string WhyNot =
        "Wolverine runs a saga alone at the endpoint its message arrives on, so another handler of that message would never run in "
        + "the roles web and worker (W5). Send the saga a reply of its own (S13).";

    private readonly ComposedApplication _application = new("worker");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();

    [Fact]
    public void ReadTheHandlerGraph_EveryMessageASagaHandles_HasNoOtherHandler()
    {
        var graph = _application.Services.GetRequiredService<HandlerGraph>();
        var handlersByMessage = graph.Chains
            .Concat(graph.Chains.SelectMany(chain => chain.ByEndpoint))
            .GroupBy(chain => chain.MessageType)
            .ToDictionary(message => message.Key, message => message.SelectMany(chain => chain.Handlers).Select(call => call.HandlerType).Distinct().ToArray());
        var sagaMessages = handlersByMessage.Where(message => message.Value.Any(IsSaga)).ToList();

        var shared = sagaMessages
            .Where(message => message.Value.Length > 1)
            .Select(message => $"{message.Key.Name} -> {string.Join(", ", message.Value.Select(handler => handler.Name))}");

        sagaMessages.Select(message => message.Key).ShouldContain(typeof(InvitationEmailSent));
        sagaMessages.Select(message => message.Key).ShouldContain(typeof(Fault<ActivateTenant>));
        shared.ShouldBeEmpty(WhyNot);
    }

    private static bool IsSaga(Type handler) => handler.IsAssignableTo(typeof(Saga));
}
