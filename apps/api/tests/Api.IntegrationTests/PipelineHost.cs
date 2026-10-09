using System.Net.Http.Headers;
using Api.SystemAdmins;
using Api.Tenants;
using ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Infrastructure;
using Tenancy;
using Wolverine.EntityFrameworkCore;

namespace Api.IntegrationTests;

// Runs the host's real pipeline on a test server against the test database, with the test endpoints and handlers mapped
// where module endpoints go.
internal sealed class PipelineHost : IAsyncDisposable
{
    private static readonly Dictionary<string, string?> DefaultSettings = new()
    {
        ["RateLimiting:PermitLimit"] = "1000",
        ["RateLimiting:Window"] = "00:01:00",
        ["Pipeline:SlowCommandThreshold"] = "00:00:01",
        ["Host:Role"] = "all",
        ["ControlPlane:Invitations:AcceptUrl"] = ApiFactory.AcceptUrl,
        ["ControlPlane:Clerk:SecretKey"] = "sk_test_unused",
        ["ControlPlane:FirstSystemAdminEmail"] = ApiFactory.FirstSystemAdminEmail,
    };

    private readonly WebApplication _app;

    private PipelineHost(WebApplication app) => _app = app;

    public TestServer Server => _app.GetTestServer();

    public IServiceProvider Services => _app.Services;

    public IHost Host => _app;

    public static async Task<PipelineHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var database = await TestContext.Current.GetFixture<Database>()
            ?? throw new InvalidOperationException("The database fixture is not available.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(DefaultSettings);
        builder.Configuration.AddInMemoryCollection(TestTokens.Settings);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = database.ApplicationConnectionString,
        });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

        builder.AddApiPipeline(typeof(PipelineHost).Assembly);
        builder.Services.AddControlPlaneInfrastructure();
        builder.Services.AddNotificationsInfrastructure();
        builder.Services.AddDbContextWithWolverineIntegration<ProbeDbContext>(
            (provider, options) => options.UseModuleDatabase(provider, ProbeDbContext.Schema),
            TenancyServiceCollectionExtensions.MessageSchema);
        builder.Services.AddModuleMigrations<ProbeDbContext>(ProbeDbContext.Schema);
        builder.Services.TrustTestKey();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseApiPipeline();
        var v1 = app.MapV1();
        var system = v1.MapSystem();
        TestEndpoints.Map(v1);
        TestEndpoints.MapTenant(v1.MapTenant());
        TestEndpoints.MapSystem(system);
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new PipelineHost(app);
    }

    public HttpClient CreateClient() => _app.GetTestClient();

    // A client signed in as the user, with a session token the host trusts.
    public HttpClient CreateClient(string userId, bool secondFactor = false) => CreateClientWith(TestTokens.For(userId, secondFactor));

    public HttpClient CreateClientWith(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
