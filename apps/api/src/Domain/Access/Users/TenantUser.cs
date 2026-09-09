namespace Domain.Access.Users;

public enum TenantUserStatus { Active, Disabled }

public sealed class TenantUser
{
    public Guid Id { get; private set; }

    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public TenantUserStatus Status { get; private set; }

    private TenantUser()
    {
    }

    public static TenantUser Create(Guid id, ExternalUserId externalUserId, TenantUserStatus status)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant user ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(externalUserId);

        return new TenantUser
        {
            Id = id,
            ExternalUserId = externalUserId,
            Status = status
        };
    }
}
