using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Infrastructure.Provisioning;

namespace Infrastructure.Persistence;

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddTenantPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ControlPlaneDbContext>(options =>
            options.UseNpgsql(
                RequiredConnectionString(configuration, "ControlPlane"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control")));

        services.AddScoped(_ => new TenantDbContextFactory(
            RequiredConnectionString(configuration, "TenantData")));

        services.AddSingleton(new TenantProvisioner(
            RequiredConnectionString(configuration, "Provisioner")));

        services.AddScoped<TenantProvisioningHandler>();

        services.AddKeyedSingleton<NpgsqlDataSource>("control-plane-read", (_, _) =>
            NpgsqlDataSource.Create(RequiredConnectionString(configuration, "ControlPlaneRead")));

        return services;
    }

    public static IServiceCollection AddPostgresReadiness(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks();
        var connectionString = configuration.GetConnectionString("ControlPlane");

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

    internal static string RequiredConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");
}
