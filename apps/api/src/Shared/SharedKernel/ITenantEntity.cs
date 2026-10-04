namespace SharedKernel;

/// <summary>
/// Marks an entity whose rows belong to one tenant. The marker alone gives the entity its tenant column, query filter and
/// row level security policy (0014).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040", Justification = "A marker interface is the decision in 0014.")]
public interface ITenantEntity;
