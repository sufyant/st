using Domain.Shared;

namespace Domain.Authorization;

public enum UserStatus { Active, Disabled }

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public static UserId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant user ID cannot be empty.", nameof(value))
        : new UserId(value);
}

public sealed class User : Entity<UserId>
{
    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public UserStatus Status { get; private set; }

    private User()
    {
    }

    public static User Create(ExternalUserId externalUserId, UserStatus status)
    {
        ArgumentNullException.ThrowIfNull(externalUserId);

        return new User
        {
            Id = UserId.New(),
            ExternalUserId = externalUserId,
            Status = status
        };
    }

    public void Enable() => Status = UserStatus.Active;

    public void Disable() => Status = UserStatus.Disabled;
}
