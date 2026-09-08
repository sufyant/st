namespace Api.Domain;

public sealed class RolePermission : Entity<Guid>
{
    public string Role { get; private set; } = null!;

    public string Permission { get; private set; } = null!;

    private RolePermission()
    {
    }

    private RolePermission(Guid id, string role, string permission) : base(id)
    {
        Role = role;
        Permission = permission;
    }

    public static RolePermission Create(string role, string permission)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission cannot be empty.", nameof(permission));
        }

        return new RolePermission(Guid.NewGuid(), role, permission);
    }
}
