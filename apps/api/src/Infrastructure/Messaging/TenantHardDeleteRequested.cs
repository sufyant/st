namespace Infrastructure.Messaging;

public sealed record TenantHardDeleteRequested(Guid TenantId)
{
    public const string MessageType = "TenantHardDeleteRequested";
}
