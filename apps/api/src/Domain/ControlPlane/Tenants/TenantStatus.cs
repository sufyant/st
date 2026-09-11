namespace Domain.ControlPlane.Tenants;

public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    Deprovisioning,
    Deleted
}
