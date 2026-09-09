using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure.Persistence.Admin;

public sealed class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
{
    public AdminDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AdminDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");
        var systemDatabaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "systemdb"
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(
                systemDatabaseConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"))
            .Options;

        return new AdminDbContext(options);
    }
}
