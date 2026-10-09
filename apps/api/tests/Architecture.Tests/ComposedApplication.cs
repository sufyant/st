using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Architecture.Tests;

// The application as Program composes it, in the role that handles every message, without the start-up checks that need a
// database and its settings.
internal sealed class ComposedApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Host:Role", "all");
        builder.ConfigureTestServices(services =>
        {
            services
                .Where(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType?.Assembly == typeof(Program).Assembly)
                .ToList()
                .ForEach(check => services.Remove(check));
            services.RemoveAll<IStartupValidator>();
        });
    }
}
