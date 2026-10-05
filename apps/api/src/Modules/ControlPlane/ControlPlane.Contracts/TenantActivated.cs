namespace ControlPlane.Contracts;

/// <summary>
/// A tenant finished its onboarding and is active (0026). Published through the outbox (0009, 0024); a subscriber handles it in the
/// tenant's transaction, as the tenant's id travels in the message's envelope (0017).
/// </summary>
public sealed record TenantActivated(Guid TenantId, string Slug);
