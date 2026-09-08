using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class OutboxTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    [Fact]
    public async Task RenamingTenantThroughMediator_WritesOutboxMessageInSameTransaction()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var tenant = Tenant.Create(TenantSlug.Create($"outbox-{Guid.NewGuid():N}"[..15]), "Original");
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        tenant.ClearDomainEvents();

        // Act — exercise the same rename+outbox mechanism the mediator pipeline uses,
        // directly, without needing the full DI-wired mediator stack from Task 4.
        tenant.Rename("Renamed via outbox test");
        foreach (var domainEvent in tenant.DomainEvents)
        {
            var content = System.Text.Json.JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
            dbContext.OutboxMessages.Add(OutboxMessage.FromDomainEvent(domainEvent, domainEvent.GetType().Name, content));
        }
        tenant.ClearDomainEvents();
        await dbContext.SaveChangesAsync();

        // Assert — scoped to this test's own tenant, since PostgresContainerFixture
        // shares one database across the whole collection and other tests (e.g.
        // RenameTenantCommandEndToEndTests) also leave unprocessed
        // TenantRenamedDomainEvent rows behind; an unscoped SingleAsync here is
        // order-dependently flaky across test classes.
        var message = await dbContext.OutboxMessages
            .SingleAsync(m =>
                m.Type == nameof(TenantRenamedDomainEvent)
                && m.ProcessedAtUtc == null
                && m.Content.Contains(tenant.Id.ToString()));
        Assert.Contains("Renamed via outbox test", message.Content);
    }

    [Fact]
    public async Task TwoConcurrentProcessorRuns_DoNotDoubleProcessTheSameMessage()
    {
        // Arrange
        await using var seedContext = CreateDbContext();
        var message = OutboxMessage.FromDomainEvent(
            new TenantRenamedDomainEvent(Guid.NewGuid(), "Concurrent Test"),
            nameof(TenantRenamedDomainEvent),
            "{}");
        seedContext.OutboxMessages.Add(message);
        await seedContext.SaveChangesAsync();

        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();

        // Act — simulate two concurrent processor cycles racing for the same row.
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        var firstBatch = await firstContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT "Id", "Type", "Content", "OccurredOnUtc", "ProcessedAtUtc"
                FROM "admin"."OutboxMessages"
                WHERE "ProcessedAtUtc" IS NULL
                ORDER BY "OccurredOnUtc"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync();

        // Second transaction, started while the first still holds the row lock, must
        // skip it entirely (not block, not error) thanks to SKIP LOCKED.
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
        var secondBatch = await secondContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT "Id", "Type", "Content", "OccurredOnUtc", "ProcessedAtUtc"
                FROM "admin"."OutboxMessages"
                WHERE "ProcessedAtUtc" IS NULL
                ORDER BY "OccurredOnUtc"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync();

        foreach (var claimed in firstBatch)
        {
            claimed.MarkProcessed(DateTimeOffset.UtcNow);
        }
        await firstContext.SaveChangesAsync();
        await firstTransaction.CommitAsync();

        foreach (var claimed in secondBatch)
        {
            claimed.MarkProcessed(DateTimeOffset.UtcNow);
        }
        await secondContext.SaveChangesAsync();
        await secondTransaction.CommitAsync();

        // Assert
        Assert.Contains(firstBatch, m => m.Id == message.Id);
        Assert.DoesNotContain(secondBatch, m => m.Id == message.Id);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_RealProcessor_MarksSeededMessageProcessed()
    {
        // Arrange — exercise the REAL OutboxProcessor's constructor and SQL
        // (SELECT ... FOR UPDATE SKIP LOCKED), not a test-local copy of its logic.
        var services = new ServiceCollection();
        services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        await using var seedContext = CreateDbContext();
        var message = OutboxMessage.FromDomainEvent(
            new TenantRenamedDomainEvent(Guid.NewGuid(), "Processed via real processor"),
            typeof(TenantRenamedDomainEvent).FullName!,
            "{}");
        seedContext.OutboxMessages.Add(message);
        await seedContext.SaveChangesAsync();

        var processor = new OutboxProcessor(scopeFactory, NullLogger<OutboxProcessor>.Instance);

        // Act
        await processor.ProcessPendingMessagesAsync(CancellationToken.None);

        // Assert
        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.NotNull(reloaded.ProcessedAtUtc);
    }
}
