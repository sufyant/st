namespace Domain.Authorization;

public sealed class UserRole
{
    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    private UserRole()
    {
    }

    public static UserRole Create(Guid userId, Guid roleId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Tenant user ID cannot be empty.", nameof(userId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("Role ID cannot be empty.", nameof(roleId));
        }

        return new UserRole
        {
            UserId = userId,
            RoleId = roleId
        };
    }
}
