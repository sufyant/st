using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantDbContextFactory(string connectionString)
{
    public TenantDbContext Create(string schemaName)
    {
        if (!Guid.TryParseExact(schemaName, "N", out var tenantId) || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant schema name must be a non-empty GUID in N format.", nameof(schemaName));
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = $"\"{tenantId:N}\""
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

        return new TenantDbContextFactory(connectionString).Create(Guid.Parse("11111111-1111-1111-1111-111111111111").ToString("N"));
    }
}
