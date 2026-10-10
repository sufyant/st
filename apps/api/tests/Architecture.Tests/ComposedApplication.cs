using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Architecture.Tests;

// The application as Program composes it, in the given role (by default the one that handles every message), without the start-up
// checks that need a database and its settings. The roles web and worker configure the PostgreSQL transport, which needs a
// connection setting; it is never used: Wolverine's message storage and external transports are switched off, and its endpoints and
// routing stay as configured. A test may configure the host further.
internal sealed class ComposedApplication(string role = "all", Action<IWebHostBuilder>? configure = null) : WebApplicationFactory<Program>
{
    private const string UnusedConnection = "Host=localhost;Database=unused;Username=unused;Password=unused";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Host:Role", role);
        if (role != "all")
        {
            builder.UseSetting("ConnectionStrings:Database", UnusedConnection);
        }

        builder.ConfigureTestServices(services =>
        {
            services
                .Where(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType?.Assembly == typeof(Program).Assembly)
                .ToList()
                .ForEach(check => services.Remove(check));
            services.RemoveAll<IStartupValidator>();
            if (role != "all")
            {
                services.DisableAllExternalWolverineTransports();
                services.DisableAllWolverineMessagePersistence();
            }
        });

        configure?.Invoke(builder);
    }
}
