using ControlPlane.Contracts;
using Notifications.Contracts;
using SharedKernel;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace ControlPlane.Application.Tenants;

public enum TenantOnboardingState
{
    Activating,
    SendingInvitation,
    Completed,
    Cancelling,
    Cancelled,
    NeedsAttention,
}

/// <summary>How long the onboarding waits for the activation, the invitation email and the cancellation (S10).</summary>
public sealed record OnboardingTimeouts(TimeSpan Activation, TimeSpan InvitationEmail, TimeSpan Cancellation);

/// <summary>
/// The tenant onboarding process of section 6, orchestrated in one place (S3, S4). Its id is the tenant's id, and its record belongs
/// to the tenant. It only decides: from its state and a message it chooses its next state and the messages to send, and it calls
/// nothing (S12). A message that does not fit its state is ignored (S7). The record stays when the process ends, so a late or
/// repeated message still finds the state it is ignored by, and the record says where the process ended.
/// </summary>
/// <remarks>
/// Steps: start (compensatable), activate the tenant (pivot), send the invitation email (retryable) (S5). A step that fails for good
/// reaches the saga as Wolverine's fault of its message. Every wait has a timeout (S10). A wait that times out while the invitation
/// may still be open cancels it, so a late email carries a link that answers 404. Completed, Cancelled and NeedsAttention are final:
/// the alarm is raised once for each onboarding.
/// </remarks>
public sealed class TenantOnboarding : Saga, ITenantEntity
{
    private const string ActivationFailed = "activation_failed";

    private TenantOnboarding()
    {
    }

    public Guid Id { get; private set; }

    public TenantOnboardingState State { get; private set; }

    public Guid InvitationId { get; private set; }

    /// <summary>Fixed when the onboarding starts, so a configuration change does not move a deadline already set.</summary>
    public TimeSpan InvitationEmailTimeout { get; private set; }

    /// <summary>Fixed when the onboarding starts, like <see cref="InvitationEmailTimeout"/>.</summary>
    public TimeSpan CancellationTimeout { get; private set; }

    /// <summary>Step 1 starts the onboarding: the tenant is activated with the link the owner is invited by, within the activation timeout.</summary>
    public static (TenantOnboarding Onboarding, ActivateTenant Activate, ActivationTimedOut Timeout) Begin(
        Guid tenantId,
        Guid invitationId,
        Uri acceptLink,
        OnboardingTimeouts timeouts) =>
        (
            new TenantOnboarding
            {
                Id = tenantId,
                State = TenantOnboardingState.Activating,
                InvitationId = invitationId,
                InvitationEmailTimeout = timeouts.InvitationEmail,
                CancellationTimeout = timeouts.Cancellation,
            },
            new ActivateTenant(tenantId, invitationId, acceptLink),
            new ActivationTimedOut(tenantId, timeouts.Activation));

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(TenantActivated.TenantId))] TenantActivated activated) =>
        State == TenantOnboardingState.Activating
            ? MoveTo(TenantOnboardingState.SendingInvitation, new InvitationEmailTimedOut(Id, InvitationEmailTimeout))
            : [];

    public OutgoingMessages Handle(Fault<ActivateTenant> fault) =>
        State == TenantOnboardingState.Activating
            ? MoveTo(
                TenantOnboardingState.Cancelling,
                new CancelTenant(Id, InvitationId, ActivationFailed),
                new CancellationTimedOut(Id, CancellationTimeout))
            : [];

    // The activation may still commit later; the invitation is closed meanwhile, so the email it sends leads nowhere.
    public OutgoingMessages Handle([SagaIdentityFrom(nameof(ActivationTimedOut.TenantId))] ActivationTimedOut timeout) =>
        State == TenantOnboardingState.Activating ? NeedsAttentionWithTheInvitationCancelled() : [];

    // The email can be reported before the activation reaches the saga: both are published by the activation, so the report proves
    // that the activation committed.
    public OutgoingMessages Handle([SagaIdentityFrom(nameof(InvitationEmailSent.TenantId))] InvitationEmailSent sent) =>
        IsSendingInvitation ? MoveTo(TenantOnboardingState.Completed) : [];

    // After the pivot nothing is undone: the tenant stays active, and the invitation nobody received is withdrawn.
    public OutgoingMessages Handle(Fault<OwnerInvitationReady> fault) =>
        IsSendingInvitation ? NeedsAttentionWithTheInvitationCancelled() : [];

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(InvitationEmailTimedOut.TenantId))] InvitationEmailTimedOut timeout) =>
        State == TenantOnboardingState.SendingInvitation ? NeedsAttentionWithTheInvitationCancelled() : [];

    public OutgoingMessages Handle([SagaIdentityFrom(nameof(TenantCancelled.TenantId))] TenantCancelled cancelled) =>
        State == TenantOnboardingState.Cancelling ? MoveTo(TenantOnboardingState.Cancelled) : [];

    // An active tenant is never cancelled: the compensation reports nothing, and its wait times out.
    public OutgoingMessages Handle([SagaIdentityFrom(nameof(CancellationTimedOut.TenantId))] CancellationTimedOut timeout) =>
        State == TenantOnboardingState.Cancelling ? NeedsAttention() : [];

    // A compensation that fails for good leaves the process to a person (S9).
    public OutgoingMessages Handle(Fault<CancelTenant> fault) => State == TenantOnboardingState.Cancelling ? NeedsAttention() : [];

    public OutgoingMessages Handle(Fault<CancelInvitation> fault) => IsFinal ? [] : NeedsAttention();

    private bool IsSendingInvitation => State is TenantOnboardingState.Activating or TenantOnboardingState.SendingInvitation;

    private bool IsFinal => State is TenantOnboardingState.Completed or TenantOnboardingState.Cancelled or TenantOnboardingState.NeedsAttention;

    private OutgoingMessages NeedsAttentionWithTheInvitationCancelled() =>
        MoveTo(TenantOnboardingState.NeedsAttention, new CancelInvitation(Id, InvitationId), new RaiseOnboardingAlarm(Id));

    private OutgoingMessages NeedsAttention() => MoveTo(TenantOnboardingState.NeedsAttention, new RaiseOnboardingAlarm(Id));

    private OutgoingMessages MoveTo(TenantOnboardingState state, params object[] messages)
    {
        State = state;
        return [.. messages];
    }
}
