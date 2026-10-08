namespace SharedKernel;

/// <summary>
/// Marks an entity whose rows belong to one tenant. The marker alone gives the entity its tenant column, query filter and
/// row level security policy.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040", Justification = "The marker alone is what makes an entity a tenant entity.")]
public interface ITenantEntity;
