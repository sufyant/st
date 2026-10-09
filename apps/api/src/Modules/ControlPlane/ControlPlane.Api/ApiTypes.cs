namespace ControlPlane.Api;

// The API's own types: what clients send and receive, kept apart from the commands they become.

/// <summary>The invitation code from the invitation link.</summary>
public sealed record AcceptInvitationRequest(string Code);

public sealed record AcceptedInvitationResponse(string TenantSlug);

/// <summary>A new tenant, and the email address of its first owner, who is invited once the tenant is ready.</summary>
public sealed record CreateTenantRequest(string Name, string Slug, string OwnerEmail);

/// <summary>A tenant and where its onboarding stands: provisioning, active or failed.</summary>
public sealed record TenantResponse(Guid Id, string Name, string Slug, string Status);

/// <summary>A member of the tenant: the user's id and their role, Owner, Admin or Member.</summary>
public sealed record MemberResponse(Guid UserId, string Role);
