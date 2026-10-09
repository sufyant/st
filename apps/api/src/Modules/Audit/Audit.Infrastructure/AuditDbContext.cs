using Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace Audit.Infrastructure;

/// <summary>The <c>audit</c> schema: one table of audit entries with a tenant column, under row level security.</summary>
internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : TenantDbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("entries");
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.ActorId).HasMaxLength(AuditEntry.ActorIdMaxLength);
            entry.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
            entry.Property(e => e.Operation).HasMaxLength(AuditEntry.OperationMaxLength);
            entry.Property(e => e.Details).HasColumnType("jsonb");
            entry.HasIndex(e => e.OccurredAt);
        });
    }
}
