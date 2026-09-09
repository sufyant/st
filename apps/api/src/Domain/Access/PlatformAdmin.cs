namespace Domain.Access;

public sealed class PlatformAdmin
{
    public Guid Id { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    private PlatformAdmin()
    {
    }

    public static PlatformAdmin Create(Guid id, ExternalUserId externalUserId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Platform admin ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(externalUserId);

        return new PlatformAdmin
        {
            Id = id,
            ExternalUserId = externalUserId,
            CreatedAt = createdAt
        };
    }
}
