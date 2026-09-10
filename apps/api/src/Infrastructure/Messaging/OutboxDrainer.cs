using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging;

public sealed class OutboxDrainer(
    ControlPlaneDbContext dbContext,
    IEnumerable<IOutboxMessageHandler> handlers,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private const int BatchSize = 10;

    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        // The row lock is held for the whole batch so a second worker skips these messages, and so
        // that a crashed process rolls back and releases them without any bookkeeping of its own.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var messages = await dbContext.OutboxMessages
            .FromSql($"""
                SELECT * FROM control.outbox_messages
                WHERE processed_at IS NULL
                  AND attempt_count < {OutboxMessage.MaximumAttempts}
                  AND next_attempt_at <= {timeProvider.GetUtcNow()}
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        var processed = 0;

        foreach (var message in messages)
        {
            try
            {
                var handler = handlers.SingleOrDefault(candidate => candidate.MessageType == message.Type)
                              ?? throw new InvalidOperationException(
                                  $"No handler is registered for outbox message type '{message.Type}'.");
                await handler.HandleAsync(message.Payload, cancellationToken);
                message.MarkProcessed(timeProvider.GetUtcNow());
                processed++;
            }
            catch (Exception exception)
            {
                message.RecordFailure(exception.Message, timeProvider.GetUtcNow());
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return processed;
    }

    public ValueTask DisposeAsync() => dbContext.DisposeAsync();
}
