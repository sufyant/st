namespace Domain.Access.Users;

public sealed class TenantUserRole
{
    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    private TenantUserRole()
    {
    }

    public static TenantUserRole Create(Guid userId, Guid roleId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Tenant user ID cannot be empty.", nameof(userId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("Role ID cannot be empty.", nameof(roleId));
        }

        return new TenantUserRole
        {
            UserId = userId,
            RoleId = roleId
        };
    }
}
