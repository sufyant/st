namespace Domain.ControlPlane.Tenants;

public enum TenantProvisioningStep
{
    CreatingDatabase,
    MigratingSchema,
    GrantingAccess,
    SeedingOwner
}
