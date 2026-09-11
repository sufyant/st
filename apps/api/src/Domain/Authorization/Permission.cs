namespace Domain.Authorization;

public readonly record struct PermissionId(Guid Value)
{
    public static PermissionId New() => new(Guid.CreateVersion7());

    public static PermissionId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Permission ID cannot be empty.", nameof(value))
        : new PermissionId(value);
}

public sealed class Permission
{
    public PermissionId Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
}
