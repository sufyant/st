using System.Collections.Concurrent;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace Api.IntegrationTests;

// One handler of one message type: a message with several handlers has one run of each (W4).
public sealed record HandlerRun(Type Message, Type Handler);

// A test-only probe: which host ran each handler of each message type. Hosts that share one record of runs show where every handler
// of a flow ran, and a handler that never ran is missing from it.
public sealed class HandlerRuns
{
    private readonly ConcurrentQueue<(string Host, HandlerRun Run)> _runs = new();

    public IEnumerable<HandlerRun> In(string host) => _runs.Where(run => run.Host == host).Select(run => run.Run);

    internal void Record(string host, HandlerRun run) => _runs.Enqueue((host, run));
}

// Puts a host's handlers on the record, each after it handled its message.
public sealed class HandlerRunsOfHost(string host, HandlerRuns runs)
{
    public static Action<IServiceCollection> Register(string host, HandlerRuns runs) => services =>
    {
        services.AddSingleton(new HandlerRunsOfHost(host, runs));
        services.ConfigureWolverine(options => options.Policies.Add(new RecordHandlerRuns()));
    };

    // Called by the generated code of every handler chain, so public.
    public void Record(Type message, Type handler) => runs.Record(host, new HandlerRun(message, handler));
}

// Every chain Wolverine builds, the chain of each separated handler included, records its own handlers after they ran.
internal sealed class RecordHandlerRuns : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            foreach (var handler in chain.Handlers.Select(call => call.HandlerType).Distinct())
            {
                chain.Postprocessors.Add(new RecordHandlerRunFrame(chain.MessageType, handler));
            }
        }
    }
}

internal sealed class RecordHandlerRunFrame(Type message, Type handler) : SyncFrame
{
    private Variable? _runs;

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        _runs = chain.FindVariable(typeof(HandlerRunsOfHost));
        yield return _runs;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.Write(
            $"{_runs!.Usage}.{nameof(HandlerRunsOfHost.Record)}(typeof({message.FullNameInCode()}), typeof({handler.FullNameInCode()}));");
        Next?.GenerateCode(method, writer);
    }
}
