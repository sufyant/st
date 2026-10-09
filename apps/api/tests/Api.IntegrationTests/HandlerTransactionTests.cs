using System.Reflection;
using ControlPlane.Application.Invitations;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;
using Wolverine.Runtime.Handlers;

namespace Api.IntegrationTests;

// W1: Wolverine's EF Core middleware owns the transaction of every handler that reaches a module DbContext, itself or through a
// port. Wolverine sees a DbContext only behind a service it builds in its generated code. Behind one it has to service-locate,
// such as a factory registration or a type its generated code cannot see, it sees nothing and opens no transaction.
public sealed class HandlerTransactionTests(Database database) : IAsyncLifetime
{
    // The tag Wolverine's EF Core integration puts on a chain whose transaction it owns (EFCorePersistenceFrameProvider, internal).
    private const string EfCoreTransaction = "uses_efcore_transaction";

    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public void ReadTheHandlerGraph_EveryChainThatReachesADbContext_HasWolverinesEfCoreTransaction()
    {
        var graph = _api.Services.GetRequiredService<HandlerGraph>();
        var container = _api.Services.GetRequiredService<IServiceContainer>();
        using var scope = _api.Services.CreateScope();
        var reach = new DbContextReach(container, scope.ServiceProvider);
        HandlerChain[] chains = [.. graph.Chains.Concat(graph.Chains.SelectMany(chain => chain.ByEndpoint)).Where(chain => chain.Handlers.Count > 0)];

        var reaching = chains.Where(chain => ServicesOf(chain).Any(reach.Reaches)).ToList();
        var withoutTransaction = reaching
            .Where(chain => !chain.IsTransactional || !chain.Tags.ContainsKey(EfCoreTransaction))
            .Select(Describe);
        var serviceLocated = reaching
            .SelectMany(chain => ServicesOf(chain)
                .Where(reach.Reaches)
                .Where(service => !DbContextReach.IsDbContext(service) && !container.ServiceDependenciesFor(service).Any(DbContextReach.IsDbContext))
                .Select(service => $"{Describe(chain)}: {service.FullName}"));

        reaching.Select(chain => chain.MessageType).ShouldContain(typeof(AcceptInvitation));
        withoutTransaction.ShouldBeEmpty();
        serviceLocated.ShouldBeEmpty();
    }

    // What Wolverine passes to the handler: its parameters, and for a handler that is not static, the handler itself.
    private static IEnumerable<Type> ServicesOf(HandlerChain chain) =>
        chain.Handlers.SelectMany(call => call.Method.GetParameters().Select(parameter => parameter.ParameterType).Append(call.HandlerType));

    private static string Describe(HandlerChain chain) =>
        $"{chain.MessageType.Name} -> {string.Join(", ", chain.Handlers.Select(call => call.HandlerType.Name))}";

    // Whether a service is a DbContext or is built on one, read from its registration the way the container builds it, not the
    // way Wolverine's code generation does. A factory registration is resolved to find the type it builds. The framework's own
    // services are not followed: no module DbContext hides behind them.
    private sealed class DbContextReach(IServiceContainer container, IServiceProvider scope)
    {
        private readonly Dictionary<Type, bool> _known = [];

        public static bool IsDbContext(Type type) => typeof(DbContext).IsAssignableFrom(type);

        public bool Reaches(Type service)
        {
            if (_known.TryGetValue(service, out var known))
            {
                return known;
            }

            _known[service] = false;
            return _known[service] = IsDbContext(service) || (!IsFramework(service) && BuiltOnADbContext(service));
        }

        private bool BuiltOnADbContext(Type service) =>
            ImplementationOf(service)?
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => Reaches(parameter.ParameterType)) ?? false;

        private Type? ImplementationOf(Type service) =>
            container.RegistrationsFor(service) switch
            {
                [.., { ImplementationType: { } type }] => type,
                [.., { ImplementationFactory: not null }] => scope.GetService(service)?.GetType(),
                _ => service.IsClass && !service.IsAbstract ? service : null,
            };

        private static bool IsFramework(Type type) =>
            type.Namespace is { } name
            && (name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("Microsoft", StringComparison.Ordinal)
                || name.StartsWith("Wolverine", StringComparison.Ordinal)
                || name.StartsWith("JasperFx", StringComparison.Ordinal));
    }
}
