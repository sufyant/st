using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.IntegrationTests;

// Runs the host's real pipeline on a test server, with the test endpoints and handlers mapped where module endpoints go.
internal sealed class PipelineHost : IAsyncDisposable
{
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

    public static async Task<PipelineHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(DefaultSettings);
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

        builder.AddApiPipeline(typeof(PipelineHost).Assembly);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseApiPipeline();
        TestEndpoints.Map(app.MapV1());
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new PipelineHost(app);
    }

    public HttpClient CreateClient() => _app.GetTestClient();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
