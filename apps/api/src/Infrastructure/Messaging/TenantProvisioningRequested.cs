namespace Infrastructure.Messaging;

public sealed record TenantProvisioningRequested(Guid TenantId, string OwnerExternalUserId);
