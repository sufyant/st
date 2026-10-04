using SharedKernel;

namespace ControlPlane.Domain.Tenants;

internal sealed class Tenant
{
    private Tenant(Guid id, string slug)
    {
        Id = id;
        Slug = slug;
        Status = TenantStatus.Provisioning;
    }

    public Guid Id { get; private init; }

    /// <summary>The tenant's name in URLs (<c>/v1/{slug}/...</c>); it cannot change for now (0015).</summary>
    public string Slug { get; private init; }

    public TenantStatus Status { get; private set; }

    public static Result<Tenant> Create(Guid id, string slug) =>
        IsUrlSafe(slug)
            ? new Tenant(id, slug)
            : Error.Validation(
                "tenant.slug_invalid",
                "A slug is 3 to 63 lowercase letters, digits and single hyphens, and starts and ends with a letter or digit.");

    public void Activate() => Status = TenantStatus.Active;

    private static bool IsUrlSafe(string slug) =>
        slug.Length is >= 3 and <= 63
        && slug.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
        && slug[0] != '-'
        && slug[^1] != '-'
        && !slug.Contains("--", StringComparison.Ordinal);
}
