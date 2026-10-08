namespace ControlPlane.Domain.Roles;

/// <summary>
/// A set of permissions a membership is assigned. Roles are built in: they belong to no tenant and their permissions come
/// from code.
/// </summary>
internal sealed class Role
{
    public const int NameMaxLength = 100;

    private Role(Guid id, string name, BuiltInRole builtIn)
    {
        Id = id;
        Name = name;
        BuiltIn = builtIn;
    }

    public Guid Id { get; private init; }

    public string Name { get; private init; }

    public BuiltInRole BuiltIn { get; private init; }

    public IReadOnlySet<string> Permissions => BuiltInRoles.PermissionsOf(BuiltIn);

    public static Role CreateBuiltIn(Guid id, BuiltInRole role) => new(id, role.ToString(), role);
}
