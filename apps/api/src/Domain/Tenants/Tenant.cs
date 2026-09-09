namespace Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; private set; }

    public TenantAlias Alias { get; private set; } = null!;

    public TenantDatabaseName DatabaseName { get; private set; } = null!;

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Tenant()
    {
    }

    public static Tenant Create(Guid id, TenantAlias alias, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(alias);

        return new Tenant
        {
            Id = id,
            Alias = alias,
            DatabaseName = TenantDatabaseName.ForTenant(id),
            Status = TenantStatus.Provisioning,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void RenameAlias(TenantAlias alias, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(alias);

        Alias = alias;
        UpdatedAt = updatedAt;
    }

    public void ChangeStatus(TenantStatus status, DateTimeOffset updatedAt)
    {
        Status = status;
        UpdatedAt = updatedAt;
    }
}
