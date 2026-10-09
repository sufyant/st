namespace ControlPlane.Domain.SystemAdmins;

/// <summary>The door provider staff use (T5): only a system admin passes, and only with a second factor verified (A6).</summary>
internal static class SystemDoor
{
    /// <summary>The system permissions the user holds behind the door, or null when the door stays closed to them.</summary>
    public static IReadOnlySet<string>? PermissionsFor(bool isSystemAdmin, bool secondFactorVerified) =>
        isSystemAdmin && secondFactorVerified ? PermissionPools.SystemPool : null;
}
