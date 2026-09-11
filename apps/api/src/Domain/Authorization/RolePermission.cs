namespace Domain.Authorization;

public sealed class RolePermission
{
    public RoleId RoleId { get; private set; }
    public PermissionId PermissionId { get; private set; }
}
