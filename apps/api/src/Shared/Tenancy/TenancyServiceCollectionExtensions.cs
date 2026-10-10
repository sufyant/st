using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>The shared schema of Wolverine's message store (W5), whose envelope tables a module DbContext writes its messages to.</summary>
    public const string MessageSchema = "wolverine";

    /// <summary>The host's part: the data source every module DbContext uses.</summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services, Func<IServiceProvider, string> pooledConnectionString) =>
        services.AddSingleton(provider => NpgsqlDataSource.Create(pooledConnectionString(provider)));

    /// <summary>
    /// A module DbContext's options: the host's data source, the module's own schema, and the tenant declared at the start of each
    /// transaction (W2). The module registers its DbContext with Wolverine's EF Core integration and these options.
    /// </summary>
    /// <remarks>
    /// Wolverine reads the model of every module DbContext while it starts, to find the one that stores a saga (W6). A host that
    /// registers no database, as the build that writes the OpenAPI document, still builds the model: it needs the provider, not a
    /// connection.
    /// </remarks>
    public static DbContextOptionsBuilder UseModuleDatabase(this DbContextOptionsBuilder options, IServiceProvider services, string schema) =>
        UseModuleConventions(
                options,
                npgsql => services.GetService<NpgsqlDataSource>() is { } dataSource
                    ? npgsql.UseNpgsql(dataSource, Configure(schema))
                    : npgsql.UseNpgsql(Configure(schema)))
            .AddInterceptors(TenantDeclarationInterceptor.Instance);

    /// <summary>A module's migrations, which the separate migration step applies.</summary>
    public static IServiceCollection AddModuleMigrations<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext =>
        services.AddSingleton<IModuleMigrator>(new ModuleMigrator<TContext>(schema));

    /// <summary>The same configuration on a plain connection string, for design-time tooling and migrations.</summary>
    public static DbContextOptions<TContext> ModuleDbContextOptions<TContext>(string schema, string connectionString)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        UseModuleConventions(options, npgsql => npgsql.UseNpgsql(connectionString, Configure(schema)));

        return options.Options;
    }

    private static DbContextOptionsBuilder UseModuleConventions(
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

        public string Schema => schema;

        public async Task<IReadOnlyList<string>> MigrateAsync(IServiceProvider services, string connectionString, CancellationToken cancellationToken)
        {
            await using var scope = services.CreateAsyncScope();
            await using var context = ActivatorUtilities.CreateInstance<TContext>(
                scope.ServiceProvider, ModuleDbContextOptions<TContext>(schema, connectionString));

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await context.Database.MigrateAsync(cancellationToken);
            return pending;
        }
    }
}
