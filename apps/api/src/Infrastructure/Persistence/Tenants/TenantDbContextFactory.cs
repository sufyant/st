using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure.Persistence.Tenants;

public sealed class TenantDbContextFactory(string connectionString)
{
    public TenantDbContext Create(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName
        };
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        return new TenantDbContext(options);
    }

    public async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)",
            connection);
        command.Parameters.AddWithValue("name", databaseName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
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
        var connectionString = configuration.GetConnectionString("TenantData")
            ?? throw new InvalidOperationException("Connection string 'TenantData' is not configured.");

        return new TenantDbContextFactory(connectionString).Create("design_time");
    }
}
