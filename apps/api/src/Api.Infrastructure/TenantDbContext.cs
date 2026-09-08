using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantDbContext : DbContext
{
    public string SchemaName { get; }

    public TenantDbContext(DbContextOptions<TenantDbContext> options, string schemaName) : base(options)
    {
        SchemaName = schemaName;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
