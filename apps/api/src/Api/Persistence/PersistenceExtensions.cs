using Microsoft.Extensions.Options;
using Tenancy;

namespace Api.Persistence;

internal static class PersistenceExtensions
{
    public const string PooledConnection = "Pooled";

    public const string DirectConnection = "Direct";

    public static WebApplicationBuilder AddPersistence(this WebApplicationBuilder builder)
    {
        // Checked on first use rather than on start: the build starts the host to write the OpenAPI document, without a database.
        // Until the setting is there, the readiness check fails and the pod receives no traffic.
        builder.Services.AddOptions<ConnectionStringOptions>()
            .BindConfiguration("ConnectionStrings")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Pooled), "ConnectionStrings:Pooled must name the application role's pooled connection.");

        builder.Services.AddTenancy(services => services.GetRequiredService<IOptions<ConnectionStringOptions>>().Value.Pooled!);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return builder;
    }

    private sealed class ConnectionStringOptions
    {
        public string? Pooled { get; set; }
    }
}
