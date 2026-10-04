using System.Security.Claims;
using Api.Tenants;
using ControlPlane.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenancy;

namespace Api.IntegrationTests;

// Runs the host's real pipeline on a test server against the test database, with the test endpoints and handlers mapped
// where module endpoints go.
internal sealed class PipelineHost : IAsyncDisposable
{
    // Authentication arrives with Clerk in a later phase; until then a test names the signed-in user in this header.
    public const string UserHeader = "X-Test-User";

    private static readonly Dictionary<string, string?> DefaultSettings = new()
    {
        ["RateLimiting:PermitLimit"] = "1000",
        ["RateLimiting:Window"] = "00:01:00",
        ["Pipeline:SlowCommandThreshold"] = "00:00:01",
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
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Pooled"] = database.ApplicationConnectionString });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

        builder.AddApiPipeline(typeof(PipelineHost).Assembly);
        builder.Services.AddControlPlaneModule();
        builder.Services.AddModuleDbContext<ProbeDbContext>(ProbeDbContext.Schema);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.Use(SignInFromHeader);
        app.UseApiPipeline();
        var v1 = app.MapV1();
        TestEndpoints.Map(v1);
        TestEndpoints.MapTenant(v1.MapTenant());
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new PipelineHost(app);
    }

    public HttpClient CreateClient() => _app.GetTestClient();

    public HttpClient CreateClient(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(UserHeader, userId);
        return client;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static Task SignInFromHeader(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Headers[UserHeader] is [{ } userId])
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        }

        return next(context);
    }
}
