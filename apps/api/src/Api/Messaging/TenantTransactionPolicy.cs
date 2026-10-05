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
// For an audited command the commit also records it in the audit log, with the value of its Result<T> (0040).
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
        var audited = typeof(IAuditedCommand).IsAssignableFrom(chain.MessageType);

        if (result is null)
        {
            return new MethodCall(
                typeof(TenantTransactionMiddleware),
                audited ? nameof(TenantTransactionMiddleware.CommitAuditedAsync) : nameof(TenantTransactionMiddleware.CommitAsync));
        }

        var commit = (audited, result.VariableType) switch
        {
            (false, _) => new MethodCall(typeof(TenantTransactionMiddleware), nameof(TenantTransactionMiddleware.CommitIfSucceededAsync)),
            (true, { IsGenericType: true } type) => new MethodCall(
                typeof(TenantTransactionMiddleware),
                typeof(TenantTransactionMiddleware)
                    .GetMethod(nameof(TenantTransactionMiddleware.CommitAuditedValueIfSucceededAsync))!
                    .MakeGenericMethod(type.GetGenericArguments())),
            (true, _) => new MethodCall(typeof(TenantTransactionMiddleware), nameof(TenantTransactionMiddleware.CommitAuditedIfSucceededAsync)),
        };
        commit.Arguments[0] = result;
        return commit;
    }
}
