using ControlPlane.Contracts;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Notifications.Application;

// An invitation email that fails is tried again after each configured pause, which grows; then the event goes to the dead letter
// queue and the onboarding saga hears of it. The handler holds no transaction while it waits.
internal sealed class InvitationEmailRetries(IOptions<NotificationsSettings> settings) : IWolverineExtension, IHandlerPolicy
{
    public void Configure(WolverineOptions options) => options.Policies.Add(this);

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var delays = settings.Value.RetryDelays.ToArray();
        foreach (var chain in chains.Where(chain => chain.MessageType == typeof(OwnerInvitationReady)))
        {
            chain.OnAnyException().RetryWithCooldown(delays);
        }
    }
}
