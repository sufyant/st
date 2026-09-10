namespace Domain.Access;

public sealed record SystemPermissionDefinition(Guid Id, string Code, string Name, string Description);

public sealed record SystemRoleDefinition(
    Guid Id,
    string Code,
    string Name,
    string Description,
    IReadOnlySet<Guid> PermissionIds);

public static class SystemAccessCatalog
{
    public static IReadOnlyList<SystemPermissionDefinition> Permissions { get; } =
    [
        new(Guid.Parse("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), "members.read", "View members", "View tenant members."),
        new(Guid.Parse("1a7bd5bd-0e5f-42aa-b0d9-a8144ea96574"), "members.manage", "Manage members", "Add and remove tenant members."),
        new(Guid.Parse("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), "roles.read", "View roles", "View tenant roles and their permissions."),
        new(Guid.Parse("3c8c6534-6ca7-4b5f-93c5-1ba3c6d0a0b9"), "roles.manage", "Manage roles", "Create and edit tenant roles."),
        new(Guid.Parse("4d1b2a8e-8cf1-41a2-9d57-e5123e4ca92f"), "invitations.manage", "Manage invitations", "Create and revoke tenant invitations.")
    ];

    public static IReadOnlyList<SystemRoleDefinition> Roles { get; } =
    [
        new(
            Guid.Parse("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124"),
            "owner",
            "Owner",
            "Full access to this tenant.",
            Permissions.Select(permission => permission.Id).ToHashSet()),
        new(
            Guid.Parse("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310"),
            "member",
            "Member",
            "Read-only access to this tenant.",
            Permissions
                .Where(permission => permission.Code is "members.read" or "roles.read")
                .Select(permission => permission.Id)
                .ToHashSet())
    ];
}
