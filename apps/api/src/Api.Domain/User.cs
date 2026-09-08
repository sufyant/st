namespace Api.Domain;

public sealed class User : AggregateRoot<Guid>, IAuditable
{
    public string ClerkUserId { get; private set; } = null!;

    public Email Email { get; private set; } = null!;

    public string TimeZoneId { get; private set; } = "UTC";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private User()
    {
    }

    private User(Guid id, string clerkUserId, Email email) : base(id)
    {
        ClerkUserId = clerkUserId;
        Email = email;
    }

    public static User Create(string clerkUserId, Email email)
    {
        if (string.IsNullOrWhiteSpace(clerkUserId))
        {
            throw new ArgumentException("Clerk user id cannot be empty.", nameof(clerkUserId));
        }

        return new User(Guid.NewGuid(), clerkUserId, email);
    }

    public void SetTimeZone(string ianaTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZoneId))
        {
            throw new ArgumentException("Timezone id cannot be empty.", nameof(ianaTimeZoneId));
        }

        TimeZoneId = ianaTimeZoneId;
    }
}
