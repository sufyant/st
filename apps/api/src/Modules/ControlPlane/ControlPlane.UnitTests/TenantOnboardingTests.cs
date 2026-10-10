using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Notifications.Contracts;
using Wolverine;

namespace ControlPlane.UnitTests;

// The onboarding saga without a host (S12): it only decides, from its state and the message, what its next state is and which
// messages it sends. A message that does not fit the state is ignored (S7). One test for each row of the saga's state table.
public class TenantOnboardingTests
{
    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");
    private static readonly Guid InvitationId = new("0199a8f0-0000-7000-8000-000000000401");
    private static readonly Guid AdminId = new("0199a8f0-0000-7000-8000-000000000301");
    private static readonly Guid EventId = new("0199a8f0-0000-7000-8000-000000000501");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri AcceptLink = new("https://app.test/invitations/accept?code=0199a8f0-0000-7000-8000-000000000201.secret");
    private static readonly OnboardingTimeouts Timeouts = new(TimeSpan.FromMinutes(10), TimeSpan.FromHours(2), TimeSpan.FromMinutes(15));
    private const string OwnerEmail = "owner@acme.test";

    // (start) | StartTenantOnboarding handled | Activating | ActivateTenant, ActivationTimedOut
    [Fact]
    public void StartOnboarding_NewTenant_ActivatesTheTenantAndSchedulesTheActivationTimeout()
    {
        var (onboarding, activate, timeout) = TenantOnboarding.Begin(TenantId, InvitationId, AcceptLink, Timeouts);

        onboarding.Id.ShouldBe(TenantId);
        onboarding.State.ShouldBe(TenantOnboardingState.Activating);
        activate.ShouldBe(new ActivateTenant(TenantId, InvitationId, AcceptLink));
        timeout.ShouldBe(new ActivationTimedOut(TenantId, TimeSpan.FromMinutes(10)));
    }

    // Activating | TenantActivated | SendingInvitation | InvitationEmailTimedOut
    [Fact]
    public void ActivateTenant_TenantActivated_SchedulesTheInvitationEmailTimeout()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(Activated());

        onboarding.State.ShouldBe(TenantOnboardingState.SendingInvitation);
        sent.ShouldBe([new InvitationEmailTimedOut(TenantId, TimeSpan.FromHours(2))]);
    }

    // Activating | Fault<ActivateTenant> | Cancelling | CancelTenant, CancellationTimedOut
    [Fact]
    public void ActivateTenant_FailsForGood_CancelsTheTenantAndSchedulesTheCancellationTimeout()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(FaultOf(new ActivateTenant(TenantId, InvitationId, AcceptLink)));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelling);
        sent.ShouldBe([new CancelTenant(TenantId, InvitationId, "activation_failed"), new CancellationTimedOut(TenantId, TimeSpan.FromMinutes(15))]);
    }

    // Activating | ActivationTimedOut | NeedsAttention | CancelInvitation, RaiseOnboardingAlarm
    [Fact]
    public void ActivateTenant_TimesOut_NeedsAttentionAndCancelsTheInvitation()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(new ActivationTimedOut(TenantId, Timeouts.Activation));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new CancelInvitation(TenantId, InvitationId), new RaiseOnboardingAlarm(TenantId)]);
    }

    // Activating | InvitationEmailSent | Completed | none. The tenant's activation and the invitation email are published together,
    // so the email may be reported sent before the saga has heard of the activation; the email proves that the activation committed.
    [Fact]
    public void SendInvitation_EmailSentBeforeTheActivationArrives_CompletesTheOnboarding()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(EmailSent());

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    // SendingInvitation | InvitationEmailSent | Completed | none
    [Fact]
    public void SendInvitation_EmailSent_CompletesTheOnboarding()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(EmailSent());

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    // Activating | Fault<OwnerInvitationReady> | NeedsAttention | CancelInvitation, RaiseOnboardingAlarm
    [Fact]
    public void SendInvitation_FailsForGoodBeforeTheActivationArrives_NeedsAttentionAndCancelsTheInvitation()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(FaultOf(InvitationReady()));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new CancelInvitation(TenantId, InvitationId), new RaiseOnboardingAlarm(TenantId)]);
    }

    // SendingInvitation | Fault<OwnerInvitationReady> | NeedsAttention | CancelInvitation, RaiseOnboardingAlarm
    [Fact]
    public void SendInvitation_FailsForGood_NeedsAttentionAndCancelsTheInvitation()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(FaultOf(InvitationReady()));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new CancelInvitation(TenantId, InvitationId), new RaiseOnboardingAlarm(TenantId)]);
    }

    // SendingInvitation | InvitationEmailTimedOut | NeedsAttention | CancelInvitation, RaiseOnboardingAlarm
    [Fact]
    public void SendInvitation_TimesOut_NeedsAttentionAndCancelsTheInvitation()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(new InvitationEmailTimedOut(TenantId, Timeouts.InvitationEmail));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new CancelInvitation(TenantId, InvitationId), new RaiseOnboardingAlarm(TenantId)]);
    }

    // Cancelling | TenantCancelled | Cancelled | none
    [Fact]
    public void CancelTenant_TenantCancelled_EndsCancelled()
    {
        var onboarding = Cancelling();

        var sent = onboarding.Handle(new TenantCancelled(TenantId));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelled);
        sent.ShouldBeEmpty();
    }

    // Cancelling | CancellationTimedOut | NeedsAttention | RaiseOnboardingAlarm
    [Fact]
    public void CancelTenant_TimesOut_NeedsAttention()
    {
        var onboarding = Cancelling();

        var sent = onboarding.Handle(new CancellationTimedOut(TenantId, Timeouts.Cancellation));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    // Cancelling | Fault<CancelTenant> | NeedsAttention | RaiseOnboardingAlarm. S9: a compensation that fails for good leaves the
    // process to a person.
    [Fact]
    public void CancelTenant_FailsForGood_NeedsAttention()
    {
        var onboarding = Cancelling();

        var sent = onboarding.Handle(FaultOf(new CancelTenant(TenantId, InvitationId, "activation_failed")));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    // Any state except NeedsAttention | Fault<CancelInvitation> | NeedsAttention | RaiseOnboardingAlarm
    [Fact]
    public void CancelInvitation_FailsForGoodWhileCancelling_NeedsAttention()
    {
        var onboarding = Cancelling();

        var sent = onboarding.Handle(FaultOf(new CancelInvitation(TenantId, InvitationId)));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    // NeedsAttention | any message | unchanged | none. M11: the alarm is raised once for each onboarding.
    [Fact]
    public void CancelInvitation_FailsForGoodAfterTheAlarm_RaisesNoSecondAlarm()
    {
        var onboarding = SendingInvitation();
        onboarding.Handle(FaultOf(InvitationReady()));

        var sent = onboarding.Handle(FaultOf(new CancelInvitation(TenantId, InvitationId)));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBeEmpty();
    }

    // Completed | any message | unchanged | none
    [Fact]
    public void SendInvitation_TimeoutAfterCompletion_IsIgnored()
    {
        var onboarding = Completed();

        var sent = onboarding.Handle(new InvitationEmailTimedOut(TenantId, Timeouts.InvitationEmail));

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    // Cancelled | any message | unchanged | none
    [Fact]
    public void CancelTenant_TimeoutAfterTheTenantIsCancelled_IsIgnored()
    {
        var onboarding = Cancelling();
        onboarding.Handle(new TenantCancelled(TenantId));

        var sent = onboarding.Handle(new CancellationTimedOut(TenantId, Timeouts.Cancellation));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelled);
        sent.ShouldBeEmpty();
    }

    // M2: the invitation was cancelled when the wait timed out, so the late fault has nothing left to do.
    [Fact]
    public void SendInvitation_FaultAfterTheTimeout_IsIgnored()
    {
        var onboarding = SendingInvitation();
        onboarding.Handle(new InvitationEmailTimedOut(TenantId, Timeouts.InvitationEmail));

        var sent = onboarding.Handle(FaultOf(InvitationReady()));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBeEmpty();
    }

    // A late email carries a link to the cancelled invitation, which answers 404; the alarm already asked for a new invitation.
    [Fact]
    public void SendInvitation_EmailSentAfterTheTimeout_IsIgnored()
    {
        var onboarding = SendingInvitation();
        onboarding.Handle(new InvitationEmailTimedOut(TenantId, Timeouts.InvitationEmail));

        var sent = onboarding.Handle(EmailSent());

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBeEmpty();
    }

    // M11: a repeated alarm reaches the saga as a repeated timeout.
    [Fact]
    public void ActivateTenant_TimeoutArrivesAgainAfterTheAlarm_RaisesNoSecondAlarm()
    {
        var onboarding = Activating();
        onboarding.Handle(new ActivationTimedOut(TenantId, Timeouts.Activation));

        var sent = onboarding.Handle(new ActivationTimedOut(TenantId, Timeouts.Activation));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void ActivateTenant_ActivationArrivesAfterTheEmailWasSent_IsIgnored()
    {
        var onboarding = Activating();
        onboarding.Handle(EmailSent());

        var sent = onboarding.Handle(Activated());

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void ActivateTenant_TimeoutAfterTheActivation_IsIgnored()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(new ActivationTimedOut(TenantId, Timeouts.Activation));

        onboarding.State.ShouldBe(TenantOnboardingState.SendingInvitation);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void SendInvitation_EmailSentAgain_SendsNothing()
    {
        var onboarding = Completed();

        var sent = onboarding.Handle(EmailSent());

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    private static TenantOnboarding Activating() => TenantOnboarding.Begin(TenantId, InvitationId, AcceptLink, Timeouts).Onboarding;

    private static TenantOnboarding SendingInvitation()
    {
        var onboarding = Activating();
        onboarding.Handle(Activated());
        return onboarding;
    }

    private static TenantOnboarding Completed()
    {
        var onboarding = SendingInvitation();
        onboarding.Handle(EmailSent());
        return onboarding;
    }

    private static TenantOnboarding Cancelling()
    {
        var onboarding = Activating();
        onboarding.Handle(FaultOf(new ActivateTenant(TenantId, InvitationId, AcceptLink)));
        return onboarding;
    }

    private static TenantActivated Activated() => new(EventId, Now, TenantId, "Acme Ltd", AdminId);

    private static OwnerInvitationReady InvitationReady() => new(EventId, Now, TenantId, InvitationId, OwnerEmail, "Acme Ltd", AcceptLink);

    private static InvitationEmailSent EmailSent() => new(EventId, Now, TenantId, InvitationId);

    // What Wolverine publishes once a message has gone to the dead letter queue; it carries the exception's type only.
    private static Fault<T> FaultOf<T>(T message)
        where T : class =>
        new(
            message,
            ExceptionInfo.From(new InvalidOperationException(), includeMessage: false, includeStackTrace: false),
            4,
            Now,
            null,
            Guid.Empty,
            TenantId.ToString(),
            null,
            new Dictionary<string, string?>());
}
