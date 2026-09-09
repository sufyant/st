using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Infrastructure.Persistence;

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresReadiness(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks();
        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            healthChecks.AddCheck(
                "postgres",
                () => HealthCheckResult.Unhealthy("PostgreSQL connection string is not configured."),
                tags: ["ready"]);

            return services;
        }

        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        healthChecks.AddCheck<PostgresReadinessHealthCheck>("postgres", tags: ["ready"]);

        return services;
    }
}
