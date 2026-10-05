using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using SharedKernel;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace Api.Messaging;

// Wraps every handler in TenantTransactionMiddleware. A plain middleware type cannot see what the handler returned: Wolverine
// binds middleware parameters by exact type, and a handler returns Result<T> as often as Result. So the policy hands the
// handler's Result to the commit itself, also when it is one element of a tuple that carries the messages the handler sends.
internal sealed class TenantTransactionPolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            chain.Middleware.Add(new MethodCall(typeof(TenantTransactionMiddleware), nameof(TenantTransactionMiddleware.BeginAsync)));
            chain.Postprocessors.Add(CommitFor(chain));
        }
    }

    private static MethodCall CommitFor(HandlerChain chain)
    {
        var result = chain.Handlers
            .SelectMany(handler => handler.Creates)
            .FirstOrDefault(returned => typeof(Result).IsAssignableFrom(returned.VariableType));

        if (result is null)
        {
            return new MethodCall(typeof(TenantTransactionMiddleware), nameof(TenantTransactionMiddleware.CommitAsync));
        }

        var commit = new MethodCall(typeof(TenantTransactionMiddleware), nameof(TenantTransactionMiddleware.CommitIfSucceededAsync));
        commit.Arguments[0] = result;
        return commit;
    }
}
