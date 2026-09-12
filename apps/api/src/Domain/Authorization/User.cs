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

public sealed class User : Entity<UserId>, IAuditable
{
    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public EmailAddress Email { get; private set; } = null!;

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Role Role { get; private set; } = null!;

    private User()
    {
    }

    public static User Create(ExternalUserId externalUserId, EmailAddress email, UserStatus status, Role role)
    {
        ArgumentNullException.ThrowIfNull(externalUserId);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(role);

        return new User
        {
            Id = UserId.New(),
            ExternalUserId = externalUserId,
            Email = email,
            Status = status,
            Role = role
        };
    }

    public void Enable() => Status = UserStatus.Active;

    public void Disable() => Status = UserStatus.Disabled;

    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        Role = role;
    }
}
