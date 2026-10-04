namespace ControlPlane.Application.Roles;

public sealed record RoleDetails(Guid Id, string Name, bool BuiltIn, IReadOnlyCollection<string> Permissions);
