using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Api.IntegrationTests;

public sealed class CommandDurationTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
    private PipelineHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await PipelineHost.StartAsync(
            new Dictionary<string, string?> { ["Pipeline:SlowCommandThreshold"] = "00:00:02" },
            services => services
                .AddSingleton(_time)
                .AddSingleton<TimeProvider>(_time)
                .AddFakeLogging());
        _client = _host.CreateClient();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task RunCommand_SlowerThanTheThreshold_LogsAWarningNamingTheCommand()
    {
        await _client.PostAsJsonAsync("/v1/slow-work", new { duration = "00:00:03" }, TestContext.Current.CancellationToken);

        var warnings = SlowCommandWarnings();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldContain(typeof(DoSlowWork).FullName!);
    }

    [Fact]
    public async Task RunCommand_WithinTheThreshold_LogsNoWarning()
    {
        await _client.PostAsJsonAsync("/v1/slow-work", new { duration = "00:00:01" }, TestContext.Current.CancellationToken);

        SlowCommandWarnings().ShouldBeEmpty();
    }

    private List<FakeLogRecord> SlowCommandWarnings() =>
        [.. _host.Services.GetFakeLogCollector().GetSnapshot().Where(record => record.Level == LogLevel.Warning && record.Message.Contains("threshold", StringComparison.Ordinal))];
}
