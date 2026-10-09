using SharedKernel;

namespace ControlPlane.Domain.Tenants;

internal sealed class Tenant
{
    public const int NameMaxLength = 100;

    private Tenant(Guid id, string name, string slug)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Status = TenantStatus.Provisioning;
    }

    public Guid Id { get; private init; }

    /// <summary>The name people see. It does not have to be unique.</summary>
    public string Name { get; private init; }

    /// <summary>A unique, URL-safe handle, given at creation. It is never part of a path (T1); it cannot change for now.</summary>
    public string Slug { get; private init; }

    public TenantStatus Status { get; private set; }

    /// <summary>The name is stored without the spaces around it.</summary>
    public static Result<Tenant> Create(Guid id, string name, string slug)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > NameMaxLength)
        {
            return Error.Validation("tenant.name_invalid", $"A name is 1 to {NameMaxLength} characters, not counting the spaces around it.");
        }

        return IsUrlSafe(slug)
            ? new Tenant(id, trimmed, slug)
            : Error.Validation(
                "tenant.slug_invalid",
                "A slug is 3 to 63 lowercase letters, digits and single hyphens, and starts and ends with a letter or digit.");
    }

    /// <summary>Ends the tenant's onboarding. Only a provisioning tenant becomes active.</summary>
    public bool Activate() => Leave(TenantStatus.Active);

    /// <summary>Compensates a failed onboarding. Only a provisioning tenant fails; an active one stays active.</summary>
    public bool Fail() => Leave(TenantStatus.Failed);

    private bool Leave(TenantStatus status)
    {
        if (Status != TenantStatus.Provisioning)
        {
            return false;
        }

        Status = status;
        return true;
    }

    private static bool IsUrlSafe(string slug) =>
        slug.Length is >= 3 and <= 63
        && slug.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
        && slug[0] != '-'
        && slug[^1] != '-'
        && !slug.Contains("--", StringComparison.Ordinal);
}
