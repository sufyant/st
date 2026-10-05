using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace WolverineRlsSpike;

public class Note
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
}

// Whatever the interceptor needs to know at TransactionStarted time.
public interface ITenantAware
{
    string? CurrentTenantId { get; }
}

// IMessageContext is service-located from the handler's primed scope, so it is the handler's own context
// and its TenantId is Envelope.TenantId. It is read lazily, when the transaction starts.
public class SpikeDbContext(DbContextOptions<SpikeDbContext> options, IMessageContext messaging)
    : DbContext(options), ITenantAware
{
    public string? CurrentTenantId => messaging.TenantId;

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<SpikeSaga> Sagas => Set<SpikeSaga>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(b =>
        {
            b.ToTable("notes", "app");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Text).HasColumnName("text");
        });

        modelBuilder.Entity<SpikeSaga>(b =>
        {
            b.ToTable("spike_sagas", "app");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Steps).HasColumnName("steps");
            // Not done by Wolverine for EF Core sagas: without it the Version check is never in the WHERE clause.
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        });
    }
}

public class AuditDbContext(DbContextOptions<AuditDbContext> options, IMessageContext messaging)
    : DbContext(options), ITenantAware
{
    public string? CurrentTenantId => messaging.TenantId;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("entries", "audit");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Text).HasColumnName("text");
        });
    }
}
