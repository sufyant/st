namespace ControlPlane.Contracts;

/// <summary>
/// A tenant's onboarding passed its pivot: the tenant is active. Published through the outbox; a subscriber handles it in the
/// tenant's transaction, as the tenant's id travels in the message's envelope.
/// </summary>
/// <param name="EventId">Chosen by the publisher, so a subscriber that receives the event twice can tell.</param>
/// <param name="CreatedBy">The catalog user id of the system admin who started the onboarding.</param>
public sealed record TenantActivated(Guid EventId, DateTimeOffset OccurredAt, Guid TenantId, string Name, Guid CreatedBy);
