namespace Notifications.Contracts;

/// <summary>The invitation email went to the email service. Published through the outbox, in the invitation's tenant.</summary>
/// <param name="EventId">Chosen by the publisher, so a subscriber that receives the event twice can tell.</param>
public sealed record InvitationEmailSent(Guid EventId, DateTimeOffset OccurredAt, Guid TenantId, Guid InvitationId);
