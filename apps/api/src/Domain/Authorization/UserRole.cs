namespace Domain.Authorization;

public sealed class UserRole
{
    public UserId UserId { get; private set; }

    public RoleId RoleId { get; private set; }

    private UserRole()
    {
    }

    public static UserRole Create(UserId userId, RoleId roleId) =>
        new()
        {
            UserId = userId,
            RoleId = roleId
        };
}
