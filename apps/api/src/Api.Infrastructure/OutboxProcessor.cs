using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure;

public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox processing failed for this cycle.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AdminDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await dbContext.OutboxMessages
            .FromSqlRaw(
                """
                SELECT "Id", "Type", "Content", "OccurredOnUtc", "ProcessedAtUtc"
                FROM "admin"."OutboxMessages"
                WHERE "ProcessedAtUtc" IS NULL
                ORDER BY "OccurredOnUtc"
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        // "Processing" in this phase only means marking the row processed -- no
        // actual consumer/dispatcher exists yet; that's future work once something
        // needs to react to outbox events.
        foreach (var message in messages)
        {
            message.MarkProcessed(DateTimeOffset.UtcNow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
