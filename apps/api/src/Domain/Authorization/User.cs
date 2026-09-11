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
    private readonly List<Role> roles = [];

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public EmailAddress Email { get; private set; } = null!;

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Role> Roles => roles;

    private User()
    {
    }

    public static User Create(ExternalUserId externalUserId, EmailAddress email, UserStatus status)
    {
        ArgumentNullException.ThrowIfNull(externalUserId);
        ArgumentNullException.ThrowIfNull(email);

        return new User
        {
            Id = UserId.New(),
            ExternalUserId = externalUserId,
            Email = email,
            Status = status
        };
    }

    public void Enable() => Status = UserStatus.Active;

    public void Disable() => Status = UserStatus.Disabled;

    public void AssignRoles(IEnumerable<Role> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        roles.Clear();
        roles.AddRange(replacement);
    }
}
