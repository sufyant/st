namespace Domain.Access.Users;

public enum TenantUserStatus { Active, Disabled }

public sealed class TenantUser
{
    public Guid Id { get; private set; }
    public ExternalUserId ExternalUserId { get; private set; } = null!;
    public TenantUserStatus Status { get; private set; }
}
