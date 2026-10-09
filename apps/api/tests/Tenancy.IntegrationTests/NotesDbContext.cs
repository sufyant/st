using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Wolverine;

namespace Tenancy.IntegrationTests;

// A stand-in for a module's tenant entity: it carries nothing but the marker, so whatever isolates it comes from Tenancy.
internal sealed class Note : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Text { get; set; }
}

internal sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, IMessageContext? messaging = null)
    : TenantDbContext(options, messaging)
{
    public const string Schema = "fixture";

    public DbSet<Note> Notes => Set<Note>();

    protected override void BuildModel(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}
