namespace Tenancy;

/// <summary>Applies one module's migrations; run by the separate migration step, never on application start.</summary>
public interface IModuleMigrator
{
    /// <summary>The module's DbContext, the one whose migrations this applies.</summary>
    Type DbContextType { get; }

    /// <summary>The module's own schema.</summary>
    string Schema { get; }

    /// <summary>Applies the migrations the database does not have yet, and returns their names.</summary>
    Task<IReadOnlyList<string>> MigrateAsync(IServiceProvider services, string connectionString, CancellationToken cancellationToken);
}
