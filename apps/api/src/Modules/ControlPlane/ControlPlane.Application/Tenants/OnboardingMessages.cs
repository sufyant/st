using Wolverine;

namespace ControlPlane.Application.Tenants;

// The messages of the tenant onboarding saga inside ControlPlane. Each carries the new tenant, which is also the saga's id; the
// envelope carries the same tenant, so every step runs in it. The events that leave the module are in ControlPlane.Contracts and
// Notifications.Contracts.

/// <summary>Step 2: tells the identity provider that the owner's email may sign up. The accept link carries the invitation's secret.</summary>
public sealed record RegisterOwnerWithIdentityProvider(Guid TenantId, Guid InvitationId, string Email, Uri AcceptLink);

/// <summary>
/// The identity provider knows the owner: <paramref name="Link"/> is the link to send them, and
/// <paramref name="IdentityProviderInvitationId"/> the provider's invitation behind it, if one was needed.
/// </summary>
public sealed record OwnerRegistered(Guid TenantId, Guid InvitationId, Uri Link, string? IdentityProviderInvitationId);

/// <summary>Step 3, the pivot: makes the tenant active and announces it, with the link the owner is invited by.</summary>
public sealed record ActivateTenant(Guid TenantId, Guid InvitationId, Uri Link);

/// <summary>Compensation before the pivot: cancels the tenant, with a fixed reason code, and its owner's invitation.</summary>
public sealed record CancelTenant(Guid TenantId, Guid InvitationId, string Reason);

/// <summary>The tenant is cancelled.</summary>
public sealed record TenantCancelled(Guid TenantId);

/// <summary>Compensation of step 2: withdraws the identity provider's invitation, if the registration created one.</summary>
public sealed record RevokeOwnerRegistration(Guid TenantId, string? IdentityProviderInvitationId);

/// <summary>Withdraws the owner's invitation after the pivot, when nobody could be told of it.</summary>
public sealed record CancelInvitation(Guid TenantId, Guid InvitationId);

/// <summary>Tells a person that an onboarding needs them (S9).</summary>
public sealed record RaiseOnboardingAlarm(Guid TenantId);

/// <summary>The owner's registration took too long (S10).</summary>
public sealed record RegistrationTimedOut(Guid TenantId, TimeSpan DelayTime) : TimeoutMessage(DelayTime);

/// <summary>The invitation email took too long (S10).</summary>
public sealed record InvitationEmailTimedOut(Guid TenantId, TimeSpan DelayTime) : TimeoutMessage(DelayTime);
