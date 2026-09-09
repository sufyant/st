namespace Infrastructure.Messaging;

public sealed class OutboxMessage
{
    public const int MaximumAttempts = 5;

    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(10);

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage()
    {
    }

    public static OutboxMessage Create(Guid id, string type, string payload, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new OutboxMessage
        {
            Id = id,
            Type = type,
            Payload = payload,
            CreatedAt = createdAt,
            NextAttemptAt = createdAt
        };
    }

    public void MarkProcessed(DateTimeOffset processedAt) => ProcessedAt = processedAt;

    public void RecordFailure(string error, DateTimeOffset now)
    {
        AttemptCount++;
        LastError = error;
        NextAttemptAt = now + BaseBackoff * Math.Pow(2, AttemptCount - 1);
    }
}
