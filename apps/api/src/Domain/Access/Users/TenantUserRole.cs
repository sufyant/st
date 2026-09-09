namespace Domain.Access.Users;

public sealed class TenantUserRole
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
