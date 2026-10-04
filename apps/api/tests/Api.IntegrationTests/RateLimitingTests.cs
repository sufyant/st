using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Api.IntegrationTests;

public sealed class RateLimitingTests : IAsyncLifetime
{
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() =>
        _host = await PipelineHost.StartAsync(new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1" });

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_tenant_over_its_limit_is_rejected_with_problem_details_and_a_retry_hint()
    {
        await GetAsync("/v1/acme/ping");

        var rejected = await GetAsync("/v1/acme/ping");

        rejected.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        rejected.Response.ContentType.ShouldStartWith("application/problem+json");
        rejected.Response.Headers.RetryAfter.ToString().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_tenant_over_its_limit_does_not_limit_another_tenant()
    {
        await GetAsync("/v1/acme/ping");
        await GetAsync("/v1/acme/ping");

        var other = await GetAsync("/v1/globex/ping");

        other.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Outside_a_tenant_each_user_has_their_own_limit()
    {
        await GetAsync("/v1/ping", userId: "user_1");
        await GetAsync("/v1/ping", userId: "user_1");

        var other = await GetAsync("/v1/ping", userId: "user_2");

        other.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Outside_a_tenant_and_without_a_user_each_ip_address_has_its_own_limit()
    {
        await GetAsync("/v1/ping", ipAddress: "203.0.113.1");
        var limited = await GetAsync("/v1/ping", ipAddress: "203.0.113.1");

        var other = await GetAsync("/v1/ping", ipAddress: "203.0.113.2");

        limited.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        other.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    // Authentication arrives in a later phase, so the user is placed on the request directly.
    private Task<HttpContext> GetAsync(string path, string? userId = null, string ipAddress = "198.51.100.1") =>
        _host.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
            context.User = userId is null
                ? new ClaimsPrincipal()
                : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        }, TestContext.Current.CancellationToken);
}
