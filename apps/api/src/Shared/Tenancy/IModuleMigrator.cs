namespace Tenancy;

/// <summary>Applies one module's migrations; run by the separate migration step, never on application start.</summary>
public interface IModuleMigrator
{
    /// <summary>The module's DbContext, the one whose migrations this applies.</summary>
    Type DbContextType { get; }

    Task MigrateAsync(IServiceProvider services, string connectionString, CancellationToken cancellationToken);
}
