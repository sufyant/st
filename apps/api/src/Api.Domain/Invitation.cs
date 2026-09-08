using System.Security.Cryptography;

namespace Api.Domain;

public sealed class Invitation : AggregateRoot<Guid>, IAuditable
{
    public Guid TenantId { get; private set; }

    public Email Email { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public string Token { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Invitation()
    {
    }

    private Invitation(Guid id, Guid tenantId, Email email, string role, string token) : base(id)
    {
        TenantId = tenantId;
        Email = email;
        Role = role;
        Token = token;
    }

    public static Invitation Create(Guid tenantId, Email email, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new Invitation(Guid.NewGuid(), tenantId, email, role, token);
    }
}
