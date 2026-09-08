using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Infrastructure;

/// <summary>
/// Design-time factory used by `dotnet ef` to construct <see cref="AdminDbContext"/> without
/// requiring the startup project to reference Microsoft.EntityFrameworkCore.Design.
/// </summary>
public sealed class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
{
    public AdminDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdminDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=api;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"));

        return new AdminDbContext(optionsBuilder.Options);
    }
}
