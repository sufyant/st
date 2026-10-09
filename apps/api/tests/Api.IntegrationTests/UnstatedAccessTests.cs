using System.Net;

namespace Api.IntegrationTests;

// A5: an endpoint that states no access is refused at run time, never opened.
public sealed class UnstatedAccessTests : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task CallEndpoint_WithoutAnAccessStateAsASignedInUser_IsForbidden()
    {
        var response = await _host.CreateClient("user_ada").GetAsync("/v1/unstated", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CallEndpoint_WithoutAnAccessStateAndWithoutAToken_IsUnauthorized()
    {
        var response = await _host.CreateClient().GetAsync("/v1/unstated", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
