namespace Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; }

    public TenantAlias Alias { get; private set; }

    public string DatabaseName => $"tenant_{Id:N}";

    private Tenant(Guid id, TenantAlias alias)
    {
        Id = id;
        Alias = alias;
    }

    public static Tenant Create(Guid id, TenantAlias alias)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(alias);

        return new Tenant(id, alias);
    }

    public void RenameAlias(TenantAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        Alias = alias;
    }
}
