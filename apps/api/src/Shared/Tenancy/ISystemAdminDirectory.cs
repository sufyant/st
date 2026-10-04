namespace Tenancy;

/// <summary>Who may use the admin routes and enter tenants as a system admin (0031).</summary>
public interface ISystemAdminDirectory
{
    /// <summary>The system permissions of the user, or null when the user is not a system admin.</summary>
    Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(string externalUserId, CancellationToken cancellationToken);
}
