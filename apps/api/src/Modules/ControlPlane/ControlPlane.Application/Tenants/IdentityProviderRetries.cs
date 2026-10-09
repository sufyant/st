using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace ControlPlane.Application.Tenants;

// A call to the identity provider that fails is tried again after each configured pause, which grows; then the step goes to the
// dead letter queue and the onboarding saga compensates it. The steps hold no transaction while they wait. A pause of a minute or
// more is a scheduled retry: the step goes back to its queue with the time of its next try, so it holds no worker meanwhile. A
// shorter pause is waited out where the step runs.
internal sealed class IdentityProviderRetries(IOptions<OnboardingSettings> settings) : IWolverineExtension, IHandlerPolicy
{
    private static readonly Type[] Steps = [typeof(RegisterOwnerWithIdentityProvider), typeof(RevokeOwnerRegistration)];

    private static readonly TimeSpan LongestInlinePause = TimeSpan.FromMinutes(1);

    public void Configure(WolverineOptions options) => options.Policies.Add(this);

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var inline = settings.Value.RetryDelays.TakeWhile(delay => delay < LongestInlinePause).ToArray();
        var scheduled = settings.Value.RetryDelays.Skip(inline.Length).ToArray();
        foreach (var chain in chains.Where(chain => Steps.Contains(chain.MessageType)))
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
