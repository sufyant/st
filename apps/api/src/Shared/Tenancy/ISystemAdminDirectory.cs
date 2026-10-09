namespace Tenancy;

/// <summary>Who may use the system routes as a system admin.</summary>
public interface ISystemAdminDirectory
{
    /// <summary>The system permissions of the user, or null when the user is not a system admin.</summary>
    Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(string externalUserId, CancellationToken cancellationToken);
}
