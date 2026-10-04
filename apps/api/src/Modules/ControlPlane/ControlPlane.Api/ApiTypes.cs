namespace ControlPlane.Api;

// The API's own types (0010): what clients send and receive, kept apart from the commands they become.

/// <summary>A custom role: a name and permissions from the tenant permission catalogue.</summary>
public sealed record RoleRequest(string Name, IReadOnlyCollection<string> Permissions);

public sealed record RoleResponse(Guid Id, string Name, bool BuiltIn, IReadOnlyCollection<string> Permissions);

public sealed record ChangeMemberRoleRequest(Guid RoleId);

/// <summary>An invitation to join the tenant with a role.</summary>
public sealed record InvitationRequest(string Email, Guid RoleId);

/// <summary>An invitation as the inviter sees it; the token travels only in the link sent to the invited person.</summary>
public sealed record InvitationResponse(Guid Id, string Email, Guid RoleId, string Status, DateTimeOffset ExpiresAt);

/// <summary>The token from the invitation link.</summary>
public sealed record AcceptInvitationRequest(string Token);

public sealed record AcceptedInvitationResponse(string TenantSlug);

public sealed record TenantSummaryResponse(Guid Id, string Slug, string Status, int MemberCount);
