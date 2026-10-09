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
// queue and the onboarding saga hears of it. The handler holds no transaction while it waits. A pause of a minute or more is a
// scheduled retry: the event goes back to its queue with the time of its next try, so it holds no worker meanwhile. A shorter pause
// is waited out where the handler runs.
internal sealed class InvitationEmailRetries(IOptions<NotificationsSettings> settings) : IWolverineExtension, IHandlerPolicy
{
    private static readonly TimeSpan LongestInlinePause = TimeSpan.FromMinutes(1);

    public void Configure(WolverineOptions options) => options.Policies.Add(this);

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var inline = settings.Value.RetryDelays.TakeWhile(delay => delay < LongestInlinePause).ToArray();
        var scheduled = settings.Value.RetryDelays.Skip(inline.Length).ToArray();
        foreach (var chain in chains.Where(chain => chain.MessageType == typeof(OwnerInvitationReady)))
        {
            var retries = chain.OnAnyException();
            var next = inline.Length > 0 ? retries.RetryWithCooldown(inline).Then : retries;
            if (scheduled.Length > 0)
            {
                next.ScheduleRetry(scheduled);
            }
        }
    }
}
