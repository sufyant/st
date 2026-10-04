using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace Api.IntegrationTests;

public sealed class RequestLoggingTests : IAsyncLifetime
{
    private const string RequestLogCategory = "Serilog.AspNetCore.RequestLoggingMiddleware";

    private PipelineHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await PipelineHost.StartAsync(configureServices: services => services.AddFakeLogging());
        _client = _host.CreateClient();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_api_request_is_logged()
    {
        await _client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        RequestLogs().ShouldHaveSingleItem().Message.ShouldContain("/v1/ping");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task A_health_check_is_not_logged(string path)
    {
        await _client.GetAsync(path, TestContext.Current.CancellationToken);

        RequestLogs().ShouldBeEmpty();
    }

    private List<FakeLogRecord> RequestLogs() =>
        [.. _host.Services.GetFakeLogCollector().GetSnapshot().Where(record => record.Category == RequestLogCategory)];
}
