namespace ControlPlane.Contracts;

/// <summary>
/// A new tenant's first owner can be invited: the link is ready to be sent to them. Published through the outbox with the
/// tenant's activation.
/// </summary>
/// <param name="EventId">Chosen by the publisher, so a subscriber that receives the event twice can tell.</param>
/// <param name="Link">
/// Where the owner accepts: the accept link, or the identity provider's sign-up link that leads to it. It carries the invitation's
/// secret, which cannot be produced again later; see the deviations list in ARCHITECTURE.md.
/// </param>
public sealed record OwnerInvitationReady(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid InvitationId,
    string Email,
    string TenantName,
    Uri Link);
