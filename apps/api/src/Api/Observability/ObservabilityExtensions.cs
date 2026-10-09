using System.Globalization;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Api.Observability;

// Serilog writes to the console and forwards every event to the OpenTelemetry logger; logs, metrics and traces are exported
// over OTLP only when the environment names a target.
internal static class ObservabilityExtensions
{
    private const string WolverineActivitySource = "Wolverine";

    // Wolverine names its meter "Wolverine:{service name}".
    private const string WolverineMeters = "Wolverine*";

    // The OpenTelemetry SDK reads its other settings from the environment by these names, too.
    private const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // A module names its meter "Modules.{module}".
    private const string ModuleMeters = "Modules.*";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        // Serilog owns the console; the default providers would write every event a second time. Each host keeps its own logger
        // instead of replacing the static one, so hosts sharing a process (as in the tests) do not log into each other.
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(
            (services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture),
            preserveStaticLogger: true,
            writeToProviders: true);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddSource(WolverineActivitySource))
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter(WolverineMeters, ModuleMeters))
            .WithLogging();

        if (builder.Configuration[OtlpEndpoint] is { Length: > 0 } endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException($"{OtlpEndpoint} must be the absolute URL of the OTLP collector.");
            }

            telemetry.UseOtlpExporter();
        }

        return builder;
    }
}
