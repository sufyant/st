using System.Net;

namespace Api.IntegrationTests;

// Section 8 (API8): only the browser origins in Host:Cors:AllowedOrigins may call the API, with the methods and headers it uses and
// without credentials mode.
public sealed class CorsTests : IAsyncLifetime
{
    private const string AllowedOrigin = "https://app.test";

    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() =>
        _host = await PipelineHost.StartAsync(new Dictionary<string, string?> { ["Host:Cors:AllowedOrigins:0"] = AllowedOrigin });

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Preflight_FromAnAllowedOrigin_GetsTheCorsHeaders()
    {
        var response = await PreflightAsync(_host, AllowedOrigin);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AllowedOrigin]);
        response.Headers.GetValues("Access-Control-Allow-Methods").Single().Split(',').ShouldBe(["GET", "POST"], ignoreOrder: true);
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().Split(',').Select(header => header.Trim().ToLowerInvariant())
            .ShouldBe(["authorization", "content-type", "idempotency-key"], ignoreOrder: true);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task Preflight_FromAnotherOrigin_GetsNoCorsHeaders()
    {
        var response = await PreflightAsync(_host, "https://evil.test");

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Methods").ShouldBeFalse();
    }

    // A header the API does not use is not allowed, so the browser does not send the request.
    [Fact]
    public async Task Preflight_AskingForAHeaderTheApiDoesNotUse_IsNotAllowedTheHeader()
    {
        var response = await PreflightAsync(_host, AllowedOrigin, requestedHeaders: "x-forwarded-for");

        response.Headers.GetValues("Access-Control-Allow-Headers").Single().ShouldNotContain("x-forwarded-for", Case.Insensitive);
    }

    [Fact]
    public async Task Request_FromAnAllowedOrigin_GetsTheAllowedOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/ping") { Headers = { { "Origin", AllowedOrigin } } };

        var response = await _host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AllowedOrigin]);
    }

    // Empty means no browser origin at all.
    [Fact]
    public async Task Preflight_WithoutAllowedOrigins_GetsNoCorsHeaders()
    {
        await using var host = await PipelineHost.StartAsync();

        var response = await PreflightAsync(host, AllowedOrigin);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private static async Task<HttpResponseMessage> PreflightAsync(PipelineHost host, string origin, string requestedHeaders = "authorization,content-type,idempotency-key")
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/system/tenants")
        {
            Headers =
            {
                { "Origin", origin },
                { "Access-Control-Request-Method", "POST" },
                { "Access-Control-Request-Headers", requestedHeaders },
            },
        };

        return await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }
}
