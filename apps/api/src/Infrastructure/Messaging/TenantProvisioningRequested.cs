namespace Infrastructure.Messaging;

public sealed record TenantProvisioningRequested(Guid TenantId, string OwnerExternalUserId)
{
    public const string MessageType = "TenantProvisioningRequested";
}
