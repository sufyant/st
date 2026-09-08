namespace Api.Domain;

public sealed class OutboxMessage : Entity<Guid>
{
    public string Type { get; private set; } = null!;

    public string Content { get; private set; } = null!;

    public DateTimeOffset OccurredOnUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    private OutboxMessage()
    {
    }

    private OutboxMessage(Guid id, string type, string content, DateTimeOffset occurredOnUtc) : base(id)
    {
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
    }

    public static OutboxMessage FromDomainEvent(DomainEvent domainEvent, string type, string content) =>
        new(domainEvent.Id, type, content, domainEvent.OccurredOnUtc);

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
    }
}
