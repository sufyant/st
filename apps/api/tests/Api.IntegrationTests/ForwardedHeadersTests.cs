using System.Net;
using Microsoft.AspNetCore.Http;

namespace Api.IntegrationTests;

// The client address decides the rate limit bucket of unauthenticated requests, which is where a forwarded address matters.
public sealed class ForwardedHeadersTests
{
    [Fact]
    public async Task ForwardAddress_NoTrustedProxies_IsIgnored()
    {
        await using var host = await StartAsync(new Dictionary<string, string?>());
        await GetAsync(host, from: "198.51.100.1", forwardedFor: "203.0.113.1");

        var second = await GetAsync(host, from: "198.51.100.1", forwardedFor: "203.0.113.2");

        second.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task ForwardAddress_FromATrustedProxy_IsTheClientAddress()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1" });
        await GetAsync(host, from: "10.0.0.1", forwardedFor: "203.0.113.1");

        var second = await GetAsync(host, from: "10.0.0.1", forwardedFor: "203.0.113.2");

        second.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task ForwardAddress_FromATrustedNetwork_IsTheClientAddress()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8" });
        await GetAsync(host, from: "10.1.2.3", forwardedFor: "203.0.113.1");

        var second = await GetAsync(host, from: "10.1.2.3", forwardedFor: "203.0.113.2");

        second.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task ForwardAddress_FromAnUntrustedSender_IsIgnored()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1" });
        await GetAsync(host, from: "198.51.100.1", forwardedFor: "203.0.113.1");

        var second = await GetAsync(host, from: "198.51.100.1", forwardedFor: "203.0.113.2");

        second.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    private static Task<PipelineHost> StartAsync(Dictionary<string, string?> settings)
    {
        settings["RateLimiting:PermitLimit"] = "1";
        return PipelineHost.StartAsync(settings);
    }

    private static Task<HttpContext> GetAsync(PipelineHost host, string from, string forwardedFor) =>
        host.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/v1/ping";
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            context.Connection.RemoteIpAddress = IPAddress.Parse(from);
        }, TestContext.Current.CancellationToken);
}
