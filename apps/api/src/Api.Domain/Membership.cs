namespace Api.Domain;

public sealed class Membership : AggregateRoot<Guid>, IAuditable
{
    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    public string Role { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Membership()
    {
    }

    private Membership(Guid id, Guid userId, Guid tenantId, string role) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        Role = role;
    }

    public static Membership Create(Guid userId, Guid tenantId, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        return new Membership(Guid.NewGuid(), userId, tenantId, role);
    }

    public void ChangeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        Role = role;
    }
}
