using Domain.Shared;

namespace Domain.ControlPlane.Tenants;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant ID cannot be empty.", nameof(value))
        : new TenantId(value);
}

public sealed class Tenant : Entity<TenantId>
{
    public TenantAlias Alias { get; private set; } = null!;

    public TenantDatabaseName DatabaseName { get; private set; } = null!;

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public TenantProvisioningStep? ProvisioningStep { get; private set; }

    public string? ProvisioningError { get; private set; }

    private Tenant()
    {
    }

    public static Tenant Create(TenantAlias alias, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(alias);

        var id = TenantId.New();

        return new Tenant
        {
            Id = id,
            Alias = alias,
            DatabaseName = TenantDatabaseName.ForTenant(id),
            Status = TenantStatus.Provisioning,
            ProvisioningStep = TenantProvisioningStep.CreatingDatabase,
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

    public void RecordProvisioningProgress(TenantProvisioningStep step, DateTimeOffset updatedAt)
    {
        ProvisioningStep = step;
        ProvisioningError = null;
        UpdatedAt = updatedAt;
    }

    public void RecordProvisioningFailure(TenantProvisioningStep step, string error, DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        ProvisioningStep = step;
        ProvisioningError = error;
        UpdatedAt = updatedAt;
    }

    public void CompleteProvisioning(DateTimeOffset updatedAt)
    {
        Status = TenantStatus.Active;
        ProvisioningStep = null;
        ProvisioningError = null;
        UpdatedAt = updatedAt;
    }
}
