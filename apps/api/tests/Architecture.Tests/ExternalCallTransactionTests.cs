using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Application.Ports;
using Wolverine.Runtime.Handlers;

namespace Architecture.Tests;

// Section 3, Handlers and pipeline: a handler that calls an external service uses no DbContext, so no transaction is open during
// the call (Nygard, Chapter 5). Read from Wolverine's own handler graph of the composed application: no chain that reaches the
// identity provider or the email channel has Wolverine's EF Core transaction.
public sealed class ExternalCallTransactionTests : IAsyncLifetime
{
    // The tag Wolverine's EF Core integration puts on a chain whose transaction it owns (EFCorePersistenceFrameProvider, internal).
    private const string EfCoreTransaction = "uses_efcore_transaction";

    private static readonly Type[] ExternalServices = [typeof(IIdentityProvider), typeof(IEmailChannel)];

    private readonly ComposedApplication _application = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();

    [Fact]
    public void ReadTheHandlerGraph_EveryChainThatCallsAnExternalService_HasNoEfCoreTransaction()
    {
        var graph = _application.Services.GetRequiredService<HandlerGraph>();
        var container = _application.Services.GetRequiredService<IServiceContainer>();
        HandlerChain[] chains = [.. graph.Chains.Concat(graph.Chains.SelectMany(chain => chain.ByEndpoint)).Where(chain => chain.Handlers.Count > 0)];

        var calling = chains.Where(chain => ServicesOf(chain).Any(service => Reaches(service, container, []))).ToList();
        var withTransaction = calling.Where(chain => chain.Tags.ContainsKey(EfCoreTransaction)).Select(Describe);

        calling.Select(chain => chain.MessageType).ShouldBe(
            [typeof(RegisterOwnerWithIdentityProvider), typeof(RevokeOwnerRegistration), typeof(OwnerInvitationReady)],
            ignoreOrder: true);
        withTransaction.ShouldBeEmpty();
    }

    // What Wolverine passes to the handler: its parameters, and for a handler that is not static, the handler itself.
    private static IEnumerable<Type> ServicesOf(HandlerChain chain) =>
        chain.Handlers.SelectMany(call => call.Method.GetParameters().Select(parameter => parameter.ParameterType).Append(call.HandlerType));

    // Whether the service is an external service, or is built on one.
    private static bool Reaches(Type service, IServiceContainer container, HashSet<Type> seen) =>
        ExternalServices.Contains(service)
        || (seen.Add(service) && container.ServiceDependenciesFor(service).Any(dependency => Reaches(dependency, container, seen)));

    private static string Describe(HandlerChain chain) =>
        $"{chain.MessageType.Name} -> {string.Join(", ", chain.Handlers.Select(call => call.HandlerType.Name))}";

    // The application as Program composes it, without the start-up checks that need a database.
    private sealed class ComposedApplication : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureTestServices(services => services
                .Where(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType?.Assembly == typeof(Program).Assembly)
                .ToList()
                .ForEach(check => services.Remove(check)));
        }
    }
}
