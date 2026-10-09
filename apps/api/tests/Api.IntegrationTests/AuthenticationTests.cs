using System.Net;
using System.Net.Http.Json;

namespace Api.IntegrationTests;

// Clerk only authenticates: the host accepts its session tokens and nothing else.
public sealed class AuthenticationTests : IAsyncLifetime
{
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Authenticate_ValidSessionToken_IdentifiesTheUserBySubject()
    {
        var response = await _host.CreateClient("user_ada").GetAsync("/v1/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>(TestContext.Current.CancellationToken)).ShouldBe("user_ada");
    }

    // Mobile clients send no Origin, so Clerk leaves the authorized party out.
    [Fact]
    public async Task Authenticate_TokenWithoutAuthorizedParty_IsAccepted()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", authorizedParty: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authenticate_WithoutAToken_IsUnauthorizedWithProblemDetails()
    {
        var response = await _host.CreateClient().GetAsync("/v1/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Authenticate_ExpiredToken_IsUnauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", expiresIn: TimeSpan.FromMinutes(-1)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticate_TokenFromAnotherIssuer_IsUnauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", issuer: "https://clerk.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticate_TokenSignedWithAnUntrustedKey_IsUnauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", trusted: false));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticate_TokenIssuedToAnUnknownParty_IsUnauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", authorizedParty: "https://evil.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> WhoAmIAsync(string token) =>
        _host.CreateClientWith(token).GetAsync("/v1/whoami", TestContext.Current.CancellationToken);
}
