using Domain.Shared;

namespace Domain.ControlPlane.Tenants;

public readonly record struct TenantCredentialId(Guid Value)
{
    public static TenantCredentialId New() => new(Guid.CreateVersion7());

    public static TenantCredentialId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant credential ID cannot be empty.", nameof(value))
        : new TenantCredentialId(value);
}

public sealed class TenantCredential : Entity<TenantCredentialId>, IAuditable
{
    public const string ProtectionPurpose = "Domain.ControlPlane.Tenants.TenantCredential";

    public TenantId TenantId { get; private set; }

    public TenantRoleName RoleName { get; private set; } = null!;

    public string EncryptedPassword { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantCredential()
    {
    }

    public static TenantCredential Create(TenantId tenantId, TenantRoleName roleName, string encryptedPassword)
    {
        ArgumentNullException.ThrowIfNull(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);

        return new TenantCredential
        {
            Id = TenantCredentialId.New(),
            TenantId = tenantId,
            RoleName = roleName,
            EncryptedPassword = encryptedPassword
        };
    }

    public void Rotate(string encryptedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);

        EncryptedPassword = encryptedPassword;
    }
}
