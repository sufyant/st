using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Handlers;

namespace WolverineRlsSpike;

public sealed class SpikeFixture : IAsyncLifetime
{
    public Database Db { get; } = new();
    public IHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Db.StartAsync();
        Host = await SpikeHost.StartAsync(Db.AppConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        DumpGeneratedCode();
        await Host.StopAsync();
        Host.Dispose();
        await Db.DisposeAsync();
    }

    // Writes the code Wolverine generated for every handler that ran, so the real frame order can be read.
    private void DumpGeneratedCode()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../generated"));
        Directory.CreateDirectory(dir);
        var sourceCode = typeof(HandlerChain).GetProperty("SourceCode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var graph = Host.Services.GetRequiredService<HandlerGraph>();
        // Not AllChains(): it skips saga chains, whose Handlers list is cleared during codegen.
        foreach (var chain in graph.Chains.Concat(graph.Chains.SelectMany(x => x.ByEndpoint)))
        {
            if (sourceCode.GetValue(chain) is string code)
            {
                File.WriteAllText(Path.Combine(dir, chain.TypeName + ".cs"), code);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SpikeGroup : ICollectionFixture<SpikeFixture>
{
    public const string Name = "spike";
}
