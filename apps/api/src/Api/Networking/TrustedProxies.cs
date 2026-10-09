using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Api.Networking;

// Behind a reverse proxy the connection's address is the proxy's; the client's arrives in X-Forwarded-For. Only proxies named
// in configuration are believed.
internal static class TrustedProxies
{
    private const string Section = "ForwardedHeaders";

    public static IServiceCollection AddTrustedProxies(this IServiceCollection services)
    {
        services.AddOptions<TrustedProxyOptions>()
            .BindConfiguration(Section)
            .Validate(trusted => trusted.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)), $"{Section}:KnownProxies must list IP addresses.")
            .Validate(trusted => trusted.KnownNetworks.All(network => System.Net.IPNetwork.TryParse(network, out _)), $"{Section}:KnownNetworks must list networks in CIDR notation.")
            .ValidateOnStart();

        return services;
    }

    public static WebApplication UseTrustedProxies(this WebApplication app)
    {
        var trusted = app.Services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;

        // With no proxy or network listed, the middleware would believe forwarded headers from any sender, so it is left out.
        if (trusted.KnownProxies.Length == 0 && trusted.KnownNetworks.Length == 0)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in trusted.KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in trusted.KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        app.UseForwardedHeaders(options);
        return app;
    }

    private sealed class TrustedProxyOptions
    {
        public string[] KnownProxies { get; set; } = [];

        public string[] KnownNetworks { get; set; } = [];
    }
}
