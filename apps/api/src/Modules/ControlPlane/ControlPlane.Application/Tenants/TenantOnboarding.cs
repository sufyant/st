using ControlPlane.Contracts;
using Notifications.Contracts;
using SharedKernel;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace ControlPlane.Application.Tenants;

public enum TenantOnboardingState
{
    Registering,
    Activating,
    SendingInvitation,
    Completed,
    Cancelling,
    Cancelled,
    NeedsAttention,
}

/// <summary>How long the onboarding waits for the owner's registration and for the invitation email (S10).</summary>
public sealed record OnboardingTimeouts(TimeSpan Registration, TimeSpan InvitationEmail);

/// <summary>
/// The tenant onboarding process of section 6, orchestrated in one place (S3, S4). Its id is the tenant's id, and its record belongs
/// to the tenant. It only decides: from its state and a message it chooses its next state and the messages to send, and it calls
/// nothing (S12). A message that does not fit its state is ignored (S7). The record stays when the process ends, so a late or
/// repeated message still finds the state it is ignored by, and the record says where the process ended.
/// </summary>
/// <remarks>
/// Steps: register the owner with the identity provider (compensatable), activate the tenant (pivot), send the invitation email
/// (retryable) (S5). A step that fails for good reaches the saga as Wolverine's fault of its message.
/// </remarks>
public sealed class TenantOnboarding : Saga, ITenantEntity
{
    private const string IdentityProviderFailed = "identity_provider_failed";
    private const string ActivationFailed = "activation_failed";
    private const string RegistrationTimedOutReason = "registration_timed_out";

    private TenantOnboarding()
    {
    }

    public Guid Id { get; private set; }

    public TenantOnboardingState State { get; private set; }

    public Guid InvitationId { get; private set; }

    /// <summary>The identity provider's invitation the owner's registration created, if it needed one.</summary>
    public string? IdentityProviderInvitationId { get; private set; }

    /// <summary>Fixed when the onboarding starts, so a configuration change does not move a deadline already set.</summary>
    public TimeSpan InvitationEmailTimeout { get; private set; }

    /// <summary>Step 1 starts the onboarding: the owner is registered with the identity provider, within the registration timeout.</summary>
    public static (TenantOnboarding Onboarding, RegisterOwnerWithIdentityProvider Register, RegistrationTimedOut Timeout) Begin(
        Guid tenantId,
        Guid invitationId,
        string ownerEmail,
        Uri acceptLink,
        OnboardingTimeouts timeouts) =>
        (
            new TenantOnboarding
            {
                Id = tenantId,
                State = TenantOnboardingState.Registering,
                InvitationId = invitationId,
                InvitationEmailTimeout = timeouts.InvitationEmail,
            },
            new RegisterOwnerWithIdentityProvider(tenantId, invitationId, ownerEmail, acceptLink),
            new RegistrationTimedOut(tenantId, timeouts.Registration));

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(OwnerRegistered.TenantId))] OwnerRegistered registered)
    {
        if (State == TenantOnboardingState.Registering)
        {
            IdentityProviderInvitationId = registered.IdentityProviderInvitationId;
            return MoveTo(TenantOnboardingState.Activating, new ActivateTenant(Id, InvitationId, registered.Link));
        }

        // The provider's call outlived the registration timeout: what it created is undone.
        return State is TenantOnboardingState.Cancelling or TenantOnboardingState.Cancelled
            && registered.IdentityProviderInvitationId is { } created
            ? [new RevokeOwnerRegistration(Id, created)]
            : [];
    }

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(TenantActivated.TenantId))] TenantActivated activated) =>
        State == TenantOnboardingState.Activating
            ? MoveTo(TenantOnboardingState.SendingInvitation, new InvitationEmailTimedOut(Id, InvitationEmailTimeout))
            : [];

    // The email can be reported before the activation reaches the saga: both are published by the activation, so the report proves
    // that the activation committed.
    public OutgoingMessages Handle([SagaIdentityFrom(nameof(InvitationEmailSent.TenantId))] InvitationEmailSent sent) =>
        IsSendingInvitation ? MoveTo(TenantOnboardingState.Completed) : [];

    public OutgoingMessages Handle(Fault<RegisterOwnerWithIdentityProvider> fault) =>
        State == TenantOnboardingState.Registering
            ? MoveTo(TenantOnboardingState.Cancelling, new CancelTenant(Id, InvitationId, IdentityProviderFailed))
            : [];

    public OutgoingMessages Handle(Fault<ActivateTenant> fault) =>
        State == TenantOnboardingState.Activating
            ? MoveTo(
                TenantOnboardingState.Cancelling,
                new CancelTenant(Id, InvitationId, ActivationFailed),
                new RevokeOwnerRegistration(Id, IdentityProviderInvitationId))
            : [];

    // After the pivot nothing is undone: the tenant stays active, and the invitation nobody received is withdrawn.
    public OutgoingMessages Handle(Fault<OwnerInvitationReady> fault) =>
        IsSendingInvitation
            ? MoveTo(TenantOnboardingState.NeedsAttention, new CancelInvitation(Id, InvitationId), new RaiseOnboardingAlarm(Id))
            : [];

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(RegistrationTimedOut.TenantId))] RegistrationTimedOut timeout) =>
        State == TenantOnboardingState.Registering
            ? MoveTo(TenantOnboardingState.Cancelling, new CancelTenant(Id, InvitationId, RegistrationTimedOutReason))
            : [];

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(InvitationEmailTimedOut.TenantId))] InvitationEmailTimedOut timeout) =>
        State == TenantOnboardingState.SendingInvitation
            ? MoveTo(TenantOnboardingState.NeedsAttention, new RaiseOnboardingAlarm(Id))
            : [];

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(TenantCancelled.TenantId))] TenantCancelled cancelled) =>
        State == TenantOnboardingState.Cancelling ? MoveTo(TenantOnboardingState.Cancelled) : [];

    // A compensation that fails for good leaves the process to a person (S9).
    public OutgoingMessages Handle(Fault<CancelTenant> fault) => NeedsAttention();

    public OutgoingMessages Handle(Fault<RevokeOwnerRegistration> fault) => NeedsAttention();

    public OutgoingMessages Handle(Fault<CancelInvitation> fault) => NeedsAttention();

    private bool IsSendingInvitation => State is TenantOnboardingState.Activating or TenantOnboardingState.SendingInvitation;

    private OutgoingMessages NeedsAttention() => MoveTo(TenantOnboardingState.NeedsAttention, new RaiseOnboardingAlarm(Id));

    private OutgoingMessages MoveTo(TenantOnboardingState state, params object[] messages)
    {
        State = state;
        return [.. messages];
    }
}
