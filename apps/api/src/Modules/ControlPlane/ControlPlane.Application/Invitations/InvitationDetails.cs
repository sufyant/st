namespace ControlPlane.Application.Invitations;

public sealed record InvitationDetails(Guid Id, string Email, Guid RoleId, string Status, DateTimeOffset ExpiresAt);
