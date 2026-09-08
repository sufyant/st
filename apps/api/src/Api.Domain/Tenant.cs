namespace Api.Domain;

public sealed class Tenant : AggregateRoot<Guid>, IAuditable
{
    public TenantSlug Slug { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string SchemaName => $"tenant_{Slug.Value.Replace('-', '_')}";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Tenant()
    {
    }

    private Tenant(Guid id, TenantSlug slug, string name) : base(id)
    {
        Slug = slug;
        Name = name;
    }

    public static Tenant Create(TenantSlug slug, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tenant name cannot be empty.", nameof(name));
        }

        return new Tenant(Guid.NewGuid(), slug, name);
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Tenant name cannot be empty.", nameof(newName));
        }

        Name = newName;
        Raise(new TenantRenamedDomainEvent(Id, newName));
    }
}
