using SharedKernel;

namespace ControlPlane.Domain.SystemAdmins;

/// <summary>System roles hold permissions from the system pool only; more roles come when the need appears (0031).</summary>
internal static class SystemRoles
{
    public static IReadOnlySet<string> PermissionsOf(SystemRole role) => role switch
    {
        SystemRole.Administrator => Permissions.SystemPool,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Every system role has its permissions."),
    };
}
