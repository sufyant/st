namespace ControlPlane.Domain.Users;

/// <summary>One identity across tenants, matched to the identity provider's user id (0021, 0028).</summary>
internal sealed class User(Guid id, string externalId)
{
    public Guid Id { get; private init; } = id;

    public string ExternalId { get; private init; } = externalId;
}
