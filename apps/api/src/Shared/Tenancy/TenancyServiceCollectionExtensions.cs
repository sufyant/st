using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>The host's part: the pooled data source, the tenant context and the per-scope transaction (0016, 0019).</summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services, Func<IServiceProvider, string> pooledConnectionString)
    {
        services.AddSingleton(provider => NpgsqlDataSource.Create(pooledConnectionString(provider)));
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantTransaction>();

        return services;
    }

    /// <summary>A module's part: its DbContext on the scope's connection, in its own schema, and its migrations (0008).</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((provider, options) =>
        {
            var transaction = provider.GetRequiredService<TenantTransaction>();
            UseModuleDatabase(options, npgsql => npgsql.UseNpgsql(transaction.Connection, Configure(schema)))
                .AddInterceptors(new TenantTransactionEnlistment(transaction));
        });
        services.AddSingleton<IModuleMigrator>(new ModuleMigrator<TContext>(schema));

        return services;
    }

    /// <summary>The same configuration on a plain connection string, for design-time tooling and migrations.</summary>
    public static DbContextOptions<TContext> ModuleDbContextOptions<TContext>(string schema, string connectionString)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        UseModuleDatabase(options, npgsql => npgsql.UseNpgsql(connectionString, Configure(schema)));

        return options.Options;
    }

    private static DbContextOptionsBuilder UseModuleDatabase(
        DbContextOptionsBuilder options,
        Func<DbContextOptionsBuilder, DbContextOptionsBuilder> useNpgsql) =>
        useNpgsql(options)
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IMigrationsSqlGenerator, TenantMigrationsSqlGenerator>();

    private static Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder> Configure(string schema) =>
        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);

    private sealed class ModuleMigrator<TContext>(string schema) : IModuleMigrator
        where TContext : DbContext
    {
        public Type DbContextType => typeof(TContext);

        public async Task MigrateAsync(IServiceProvider services, string connectionString, CancellationToken cancellationToken)
        {
            await using var scope = services.CreateAsyncScope();
            await using var context = ActivatorUtilities.CreateInstance<TContext>(
                scope.ServiceProvider, ModuleDbContextOptions<TContext>(schema, connectionString));

            await context.Database.MigrateAsync(cancellationToken);
        }
    }
}
