namespace Infrastructure.Messaging;

public sealed record TenantProvisioningRequested(Guid TenantId, string OwnerExternalUserId, string OwnerEmail)
{
    public const string MessageType = "TenantProvisioningRequested";
}
