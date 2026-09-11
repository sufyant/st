namespace Domain.Authorization;

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.CreateVersion7());

    public static RoleId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Role ID cannot be empty.", nameof(value))
        : new RoleId(value);
}

public sealed class Role
{
    public RoleId Id { get; private set; }
    public string? Code { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
}
