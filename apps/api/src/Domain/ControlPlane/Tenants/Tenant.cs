using Domain.Shared;

namespace Domain.ControlPlane.Tenants;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant ID cannot be empty.", nameof(value))
        : new TenantId(value);
}

public sealed class Tenant : Entity<TenantId>, IAuditable
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

    public static Tenant Create(TenantAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        var id = TenantId.New();

        return new Tenant
        {
            Id = id,
            Alias = alias,
            DatabaseName = TenantDatabaseName.ForTenant(id),
            Status = TenantStatus.Provisioning,
            ProvisioningStep = TenantProvisioningStep.CreatingDatabase
        };
    }

    public void RenameAlias(TenantAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        Alias = alias;
    }

    public void RecordProvisioningProgress(TenantProvisioningStep step)
    {
        ProvisioningStep = step;
        ProvisioningError = null;
    }

    public void RecordProvisioningFailure(TenantProvisioningStep step, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        ProvisioningStep = step;
        ProvisioningError = error;
    }

    public void CompleteProvisioning()
    {
        RequireStatus(TenantStatus.Active, TenantStatus.Provisioning);

        ProvisioningStep = null;
        ProvisioningError = null;
    }

    public void Suspend() =>
        RequireStatus(TenantStatus.Suspended, TenantStatus.Active);

    public void Resume() =>
        RequireStatus(TenantStatus.Active, TenantStatus.Suspended);

    public void BeginDeprovisioning() =>
        RequireStatus(TenantStatus.Deprovisioning, TenantStatus.Active, TenantStatus.Suspended);

    public void MarkDeleted() =>
        RequireStatus(TenantStatus.Deleted, TenantStatus.Deprovisioning);

    private void RequireStatus(TenantStatus target, params TenantStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"A tenant cannot move from {Status} to {target}.");
        }

        Status = target;
    }
}
