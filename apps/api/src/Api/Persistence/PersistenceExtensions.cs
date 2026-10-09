using Microsoft.Extensions.Options;
using Tenancy;

namespace Api.Persistence;

internal static class PersistenceExtensions
{
    public const string PooledConnection = "Pooled";

    public const string DirectConnection = "Direct";

    // A host name that never resolves (RFC 2606).
    private const string Unconfigured = "Host=unconfigured.invalid";

    public static WebApplicationBuilder AddPersistence(this WebApplicationBuilder builder)
    {
        // Both connections are checked on start (DatabaseAccountCheck), which runs before Wolverine starts.
        builder.Services.AddHostedService<DatabaseAccountCheck>();
        builder.Services.AddOptions<ConnectionStringOptions>().BindConfiguration("ConnectionStrings");

        // Wolverine reads the model of every module DbContext when it starts, to find the one that stores a saga (W6). The build's
        // OpenAPI step starts the host without the setting, so there the data source names no server and is never opened; any
        // other host without the setting does not start.
        builder.Services.AddTenancy(services =>
            services.GetRequiredService<IOptions<ConnectionStringOptions>>().Value.Pooled is { Length: > 0 } pooled ? pooled : Unconfigured);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return builder;
    }

    private sealed class ConnectionStringOptions
    {
        public string? Pooled { get; set; }
    }
}
