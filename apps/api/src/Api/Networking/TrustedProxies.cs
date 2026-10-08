using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Api.Networking;

// Behind a reverse proxy the connection's address is the proxy's; the client's arrives in X-Forwarded-For. Only proxies named
// in configuration are believed.
internal static class TrustedProxies
{
    private const string Section = "ForwardedHeaders";

    public static WebApplication UseTrustedProxies(this WebApplication app)
    {
        var trusted = app.Configuration.GetSection(Section).Get<TrustedProxyOptions>() ?? new TrustedProxyOptions();

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
