using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Infrastructure.Persistence.Admin;
using Infrastructure.Persistence.Tenants;

namespace Infrastructure.Persistence;

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddTenantPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AdminDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");
            var systemDatabaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "systemdb"
            }.ConnectionString;
            options.UseNpgsql(systemDatabaseConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"));
        });
        services.AddScoped(_ =>
        {
            var connectionString = configuration
                .GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");

            return new TenantDbContextFactory(connectionString);
        });

        return services;
    }

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

        var systemDatabaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "systemdb"
        }.ConnectionString;
        services.AddSingleton(NpgsqlDataSource.Create(systemDatabaseConnectionString));
        healthChecks.AddCheck<PostgresReadinessHealthCheck>("postgres", tags: ["ready"]);

        return services;
    }
}
