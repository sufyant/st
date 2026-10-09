using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace ControlPlane.Application.Tenants;

// A call to the identity provider that fails is tried again after each configured pause, which grows; then the step goes to the
// dead letter queue and the onboarding saga compensates it. The steps hold no transaction while they wait.
internal sealed class IdentityProviderRetries(IOptions<OnboardingSettings> settings) : IWolverineExtension, IHandlerPolicy
{
    private static readonly Type[] Steps = [typeof(RegisterOwnerWithIdentityProvider), typeof(RevokeOwnerRegistration)];

    public void Configure(WolverineOptions options) => options.Policies.Add(this);

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var delays = settings.Value.RetryDelays.ToArray();
        foreach (var chain in chains.Where(chain => Steps.Contains(chain.MessageType)))
        {
            chain.OnAnyException().RetryWithCooldown(delays);
        }
    }
}
