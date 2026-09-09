namespace Domain.Access.Roles;

public sealed class TenantRolePermission
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
}
