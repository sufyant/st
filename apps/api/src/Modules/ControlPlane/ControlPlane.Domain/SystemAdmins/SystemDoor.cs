namespace ControlPlane.Domain.SystemAdmins;

/// <summary>The door provider staff use (T5): only a system admin passes, and only with a second factor verified (A6).</summary>
internal static class SystemDoor
{
    /// <summary>The system permissions the user holds behind the door, or null when the door stays closed to them.</summary>
    public static IReadOnlySet<string>? PermissionsFor(bool isSystemAdmin, bool secondFactorVerified) =>
        isSystemAdmin && secondFactorVerified ? PermissionPools.SystemPool : null;

    /// <summary>
    /// Whether the person at the door is the first system admin the configuration names (section 6): one of the addresses the
    /// identity provider has verified for them is that address, and the session verified a second factor. It counts only while the
    /// staff list is empty.
    /// </summary>
    public static bool IsFirstSystemAdmin(string firstSystemAdminEmail, bool secondFactorVerified, IEnumerable<string> verifiedEmails) =>
        secondFactorVerified && verifiedEmails.Contains(firstSystemAdminEmail, StringComparer.OrdinalIgnoreCase);
}
