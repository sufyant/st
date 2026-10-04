using ControlPlane.Domain.Roles;
using SharedKernel;

namespace ControlPlane.Application.Roles;

internal static class RoleErrors
{
    public static readonly Error NameTaken = Error.Conflict("role.name_taken", "The tenant already has a role with this name.");

    public static RoleDetails ToDetails(this Role role) =>
        new(role.Id, role.Name, role.BuiltIn is not null, [.. role.Permissions.Order(StringComparer.Ordinal)]);
}
