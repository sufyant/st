using System.Net;
using System.Net.Http.Json;

namespace Api.IntegrationTests;

// Clerk only authenticates (0028): the host accepts its session tokens and nothing else.
public sealed class AuthenticationTests : IAsyncLifetime
{
    private PipelineHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_valid_session_token_identifies_the_user_by_its_subject()
    {
        var response = await _host.CreateClient("user_ada").GetAsync("/v1/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>(TestContext.Current.CancellationToken)).ShouldBe("user_ada");
    }

    // Mobile clients send no Origin, so Clerk leaves the authorized party out.
    [Fact]
    public async Task A_token_without_an_authorized_party_is_accepted()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", authorizedParty: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthorized_with_problem_details()
    {
        var response = await _host.CreateClient().GetAsync("/v1/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task An_expired_token_is_unauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", expiresIn: TimeSpan.FromMinutes(-1)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_unauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", issuer: "https://clerk.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_an_untrusted_key_is_unauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", trusted: false));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_issued_to_an_unknown_party_is_unauthorized()
    {
        var response = await WhoAmIAsync(TestTokens.For("user_ada", authorizedParty: "https://evil.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> WhoAmIAsync(string token) =>
        _host.CreateClientWith(token).GetAsync("/v1/whoami", TestContext.Current.CancellationToken);
}
