namespace ControlPlane.Contracts;

/// <summary>
/// A tenant finished its onboarding and is active. Published through the outbox; a subscriber handles it in the
/// tenant's transaction, as the tenant's id travels in the message's envelope.
/// </summary>
public sealed record TenantActivated(Guid TenantId, string Slug);
