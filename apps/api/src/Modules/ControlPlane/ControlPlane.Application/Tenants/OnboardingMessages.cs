using Wolverine;

namespace ControlPlane.Application.Tenants;

// The messages of the tenant onboarding saga inside ControlPlane. Each carries the new tenant, which is also the saga's id; the
// envelope carries the same tenant, so every step runs in it. The events that leave the module are in ControlPlane.Contracts and
// Notifications.Contracts.

/// <summary>Step 2, the pivot: makes the tenant active and announces it, with the link the owner is invited by.</summary>
public sealed record ActivateTenant(Guid TenantId, Guid InvitationId, Uri Link);

/// <summary>Compensation before the pivot: cancels the tenant, with a fixed reason code, and its owner's invitation.</summary>
public sealed record CancelTenant(Guid TenantId, Guid InvitationId, string Reason);

/// <summary>The tenant is cancelled.</summary>
public sealed record TenantCancelled(Guid TenantId);

/// <summary>Withdraws the owner's invitation, when nobody could be told of it.</summary>
public sealed record CancelInvitation(Guid TenantId, Guid InvitationId);

/// <summary>Tells a person that an onboarding needs them (S9).</summary>
public sealed record RaiseOnboardingAlarm(Guid TenantId);

/// <summary>The activation took too long (S10).</summary>
public sealed record ActivationTimedOut(Guid TenantId, TimeSpan DelayTime) : TimeoutMessage(DelayTime);

/// <summary>The invitation email took too long (S10).</summary>
public sealed record InvitationEmailTimedOut(Guid TenantId, TimeSpan DelayTime) : TimeoutMessage(DelayTime);

/// <summary>The tenant's cancellation took too long (S10).</summary>
public sealed record CancellationTimedOut(Guid TenantId, TimeSpan DelayTime) : TimeoutMessage(DelayTime);
