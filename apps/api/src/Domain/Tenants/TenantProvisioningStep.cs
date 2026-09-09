namespace Domain.Tenants;

public enum TenantProvisioningStep
{
    CreatingDatabase,
    MigratingSchema,
    GrantingAccess,
    SeedingOwner
}
