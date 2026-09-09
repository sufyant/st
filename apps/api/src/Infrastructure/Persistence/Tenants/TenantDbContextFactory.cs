using Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantDbContextFactory(string connectionString)
{
    public TenantDbContext Create(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = tenant.DatabaseName
        };
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        return new TenantDbContext(options);
    }
}

public sealed class TenantDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<TenantDesignTimeDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");

        return new TenantDbContextFactory(connectionString).Create(
            Tenant.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), TenantAlias.Create("design-time")));
    }
}
