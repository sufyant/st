using Domain.Shared;

namespace Domain.Authorization;

public enum UserStatus { Active, Disabled }

public sealed class User
{
    public Guid Id { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public UserStatus Status { get; private set; }

    private User()
    {
    }

    public static User Create(Guid id, ExternalUserId externalUserId, UserStatus status)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant user ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(externalUserId);

        return new User
        {
            Id = id,
            ExternalUserId = externalUserId,
            Status = status
        };
    }

    public void Enable() => Status = UserStatus.Active;

    public void Disable() => Status = UserStatus.Disabled;
}
