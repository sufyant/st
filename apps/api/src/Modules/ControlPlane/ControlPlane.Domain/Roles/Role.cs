using System.Collections.Frozen;
using SharedKernel;

namespace ControlPlane.Domain.Roles;

/// <summary>
/// A set of permissions a membership is assigned (0030). Built-in roles belong to no tenant and their permissions come from code;
/// a tenant's custom roles choose their permissions from the tenant pool, never from the system pool.
/// </summary>
internal sealed class Role
{
    public const int NameMaxLength = 100;

    private static readonly Error BuiltInRoleIsFixed = Error.Validation("role.built_in", "A built-in role cannot be changed or deleted.");

    private string[] _permissions;

    private Role(Guid id, Guid? tenantId, string name, BuiltInRole? builtIn, string[] permissions)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        BuiltIn = builtIn;
        _permissions = permissions;
    }

    public Guid Id { get; private init; }

    /// <summary>The tenant a custom role belongs to; built-in roles belong to none.</summary>
    public Guid? TenantId { get; private init; }

    public string Name { get; private set; }

    public BuiltInRole? BuiltIn { get; private init; }

    public bool IsOwner => BuiltIn == BuiltInRole.Owner;

    public IReadOnlySet<string> Permissions =>
        BuiltIn is { } builtIn ? BuiltInRoles.PermissionsOf(builtIn) : _permissions.ToFrozenSet(StringComparer.Ordinal);

    public static Role CreateBuiltIn(Guid id, BuiltInRole role) => new(id, null, role.ToString(), role, []);

    public static Result<Role> CreateCustom(Guid id, Guid tenantId, string name, IEnumerable<string> permissions)
    {
        string[] chosen = [.. permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (Validate(name, chosen) is { } error)
        {
            return error;
        }

        return new Role(id, tenantId, name.Trim(), null, chosen);
    }

    public Result Change(string name, IEnumerable<string> permissions)
    {
        if (BuiltIn is not null)
        {
            return BuiltInRoleIsFixed;
        }

        string[] chosen = [.. permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (Validate(name, chosen) is { } error)
        {
            return error;
        }

        Name = name.Trim();
        _permissions = chosen;
        return Result.Success();
    }

    public Result EnsureDeletable() => BuiltIn is null ? Result.Success() : BuiltInRoleIsFixed;

    private static Error? Validate(string name, string[] permissions)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            return Error.Validation("role.name_invalid", $"A role name has 1 to {NameMaxLength} characters.");
        }

        if (BuiltInRoles.All.Any(role => string.Equals(role.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Validation("role.name_reserved", "A custom role cannot take the name of a built-in role.");
        }

        // A permission without a capability in code means nothing, and a system permission is never a tenant's to give (0031).
        if (!permissions.All(SharedKernel.Permissions.TenantPool.Contains))
        {
            return Error.Validation("role.permission_not_allowed", "A role can hold only permissions from the tenant permission catalogue.");
        }

        return null;
    }
}
