using System.Net;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Tenancy;

namespace Api.IntegrationTests;

public sealed class HostEndpointTests(Database database) : IAsyncLifetime
{
    private ApiFactory _api = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application));
        _client = _api.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The account check connects first while the application starts, so the database must be there.
    [Fact]
    public async Task Without_its_database_the_application_does_not_start()
    {
        await using var api = new ApiFactory("Host=127.0.0.1;Port=1;Username=nobody;Password=none;Timeout=1");

        var start = () => api.CreateClient();

        start.ShouldThrow<NpgsqlException>();
    }

    [Fact]
    public async Task When_its_database_goes_away_the_application_is_live_but_not_ready()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        await using var api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application, name));
        var client = api.CreateClient();
        await database.CloseAsync(name);

        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // R10: row level security does not bind a superuser, a role that bypasses it or a table's owner, so the application does not
    // start as one. Both of its connections are checked: the one requests use, and the one Wolverine keeps its messages over.
    [Fact]
    public async Task StartApplication_AsTheMigrationAccount_FailsNamingTheProblem()
    {
        await using var api = new ApiFactory(database.OwnerConnectionString, messagingConnectionString: database.ApplicationConnectionString);

        var start = () => api.CreateClient();

        start.ShouldThrow<InvalidOperationException>().Message.ShouldContain("ConnectionStrings:Database connection's role owns tables");
    }

    [Fact]
    public async Task StartApplication_AsASuperuser_FailsNamingTheProblem()
    {
        await using var api = new ApiFactory(database.SuperuserConnectionString, messagingConnectionString: database.ApplicationConnectionString);

        var start = () => api.CreateClient();

        start.ShouldThrow<InvalidOperationException>().Message.ShouldContain("ConnectionStrings:Database connection's role is a superuser");
    }

    [Fact]
    public async Task StartApplication_AsARoleThatBypassesRowLevelSecurity_FailsNamingTheProblem()
    {
        var bypassing = await database.CreateLoginRoleAsync("BYPASSRLS");
        await using var api = new ApiFactory(bypassing, messagingConnectionString: database.ApplicationConnectionString);

        var start = () => api.CreateClient();

        start.ShouldThrow<InvalidOperationException>().Message
            .ShouldContain("ConnectionStrings:Database connection's role bypasses row level security");
    }

    [Fact]
    public async Task StartApplication_AsTheMigrationAccountOverTheMessagingConnection_FailsNamingTheProblem()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, messagingConnectionString: database.OwnerConnectionString);

        var start = () => api.CreateClient();

        start.ShouldThrow<InvalidOperationException>().Message.ShouldContain("ConnectionStrings:Messaging connection's role owns tables");
    }

    // Without a connection of its own, Wolverine keeps its messages over the database connection.
    [Fact]
    public async Task StartApplication_WithoutTheMessagingConnection_KeepsMessagesOverTheDatabaseConnection()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, messagingConnectionString: null);

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StartApplication_AsTheApplicationAccount_Starts()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var live = await api.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // A role can change while the application runs; the readiness check keeps checking it.
    [Fact]
    public async Task CheckReadiness_TheRoleGainsBypassRowLevelSecurityWhileRunning_IsNotReady()
    {
        var role = await database.CreateLoginRoleAsync("");
        await using var api = new ApiFactory(role);
        var client = api.CreateClient();
        await database.ScalarAsSuperuserAsync<object>($"ALTER ROLE {new NpgsqlConnectionStringBuilder(role).Username} BYPASSRLS");

        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Outside Development the API must know which clients may use it; without the list any origin's token would be accepted.
    [Fact]
    public async Task Outside_development_the_application_does_not_start_without_authorized_parties()
    {
        await using var api = new ApiFactory(
            database.ApplicationConnectionString, environment: Environments.Production, authorizedParties: [], settings: Resend);

        var start = () => api.CreateClient();

        start.ShouldThrow<Exception>().Message.ShouldContain("Authentication:Clerk:AuthorizedParties");
    }

    [Fact]
    public async Task Outside_development_the_application_is_ready_with_authorized_parties()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, environment: Environments.Production, settings: Resend);

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task In_development_the_application_is_ready_without_authorized_parties()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, authorizedParties: []);

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_route_returns_problem_details()
    {
        var response = await _client.GetAsync("/v1/no-such-route", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task The_openapi_document_is_served_in_development()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static readonly Dictionary<string, string?> Resend = new()
    {
        ["Notifications:Resend:ApiKey"] = "re_test_key",
        ["Notifications:Resend:From"] = "no-reply@app.test",
    };

    [Fact]
    public async Task The_scalar_reference_is_served_in_development()
    {
        var response = await _client.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
