using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Notifications.Contracts;
using Wolverine;

namespace ControlPlane.UnitTests;

// The onboarding saga without a host (S12): it only decides, from its state and the message, what its next state is and which
// messages it sends. A message that does not fit the state is ignored (S7).
public class TenantOnboardingTests
{
    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");
    private static readonly Guid InvitationId = new("0199a8f0-0000-7000-8000-000000000401");
    private static readonly Guid AdminId = new("0199a8f0-0000-7000-8000-000000000301");
    private static readonly Guid EventId = new("0199a8f0-0000-7000-8000-000000000501");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri AcceptLink = new("https://app.test/invitations/accept?code=0199a8f0-0000-7000-8000-000000000201.secret");
    private static readonly Uri SignUpLink = new("https://clerk.test/invitations/inv_1");
    private static readonly OnboardingTimeouts Timeouts = new(TimeSpan.FromMinutes(10), TimeSpan.FromHours(2));
    private const string OwnerEmail = "owner@acme.test";
    private const string ProviderInvitationId = "inv_1";

    [Fact]
    public void StartOnboarding_NewTenant_RegistersTheOwnerAndSchedulesTheRegistrationTimeout()
    {
        var (onboarding, register, timeout) = TenantOnboarding.Begin(TenantId, InvitationId, OwnerEmail, AcceptLink, Timeouts);

        onboarding.Id.ShouldBe(TenantId);
        onboarding.State.ShouldBe(TenantOnboardingState.Registering);
        register.ShouldBe(new RegisterOwnerWithIdentityProvider(TenantId, InvitationId, OwnerEmail, AcceptLink));
        timeout.ShouldBe(new RegistrationTimedOut(TenantId, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void RegisterOwner_OwnerRegistered_ActivatesTheTenantWithTheLinkToSend()
    {
        var onboarding = Registering();

        var sent = onboarding.Handle(Registered());

        onboarding.State.ShouldBe(TenantOnboardingState.Activating);
        sent.ShouldBe([new ActivateTenant(TenantId, InvitationId, SignUpLink)]);
    }

    [Fact]
    public void ActivateTenant_TenantActivated_SchedulesTheInvitationEmailTimeout()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(Activated());

        onboarding.State.ShouldBe(TenantOnboardingState.SendingInvitation);
        sent.ShouldBe([new InvitationEmailTimedOut(TenantId, TimeSpan.FromHours(2))]);
    }

    [Fact]
    public void SendInvitation_EmailSent_CompletesTheOnboarding()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(EmailSent());

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void RegisterOwner_FailsForGood_CancelsTheTenant()
    {
        var onboarding = Registering();

        var sent = onboarding.Handle(FaultOf(new RegisterOwnerWithIdentityProvider(TenantId, InvitationId, OwnerEmail, AcceptLink)));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelling);
        sent.ShouldBe([new CancelTenant(TenantId, InvitationId, "identity_provider_failed")]);
    }

    [Fact]
    public void ActivateTenant_FailsForGood_CancelsTheTenantAndRevokesTheOwnersRegistration()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(FaultOf(new ActivateTenant(TenantId, InvitationId, SignUpLink)));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelling);
        sent.ShouldBe([new CancelTenant(TenantId, InvitationId, "activation_failed"), new RevokeOwnerRegistration(TenantId, ProviderInvitationId)]);
    }

    [Fact]
    public void SendInvitation_FailsForGood_NeedsAttentionAndCancelsTheInvitation()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(FaultOf(InvitationReady()));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new CancelInvitation(TenantId, InvitationId), new RaiseOnboardingAlarm(TenantId)]);
    }

    [Fact]
    public void RegisterOwner_TimesOut_CancelsTheTenant()
    {
        var onboarding = Registering();

        var sent = onboarding.Handle(new RegistrationTimedOut(TenantId, Timeouts.Registration));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelling);
        sent.ShouldBe([new CancelTenant(TenantId, InvitationId, "registration_timed_out")]);
    }

    [Fact]
    public void RegisterOwner_TimeoutAfterTheOwnerRegistered_IsIgnored()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(new RegistrationTimedOut(TenantId, Timeouts.Registration));

        onboarding.State.ShouldBe(TenantOnboardingState.Activating);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void SendInvitation_TimesOut_NeedsAttention()
    {
        var onboarding = SendingInvitation();

        var sent = onboarding.Handle(new InvitationEmailTimedOut(TenantId, Timeouts.InvitationEmail));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    [Fact]
    public void SendInvitation_TimeoutAfterCompletion_IsIgnored()
    {
        var onboarding = Completed();

        var sent = onboarding.Handle(new InvitationEmailTimedOut(TenantId, Timeouts.InvitationEmail));

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void RegisterOwner_OwnerRegisteredAgain_SendsNothing()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(Registered());

        onboarding.State.ShouldBe(TenantOnboardingState.Activating);
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

    // The tenant's activation and the invitation email are published together, so the email may be reported sent before the
    // saga has heard of the activation. The email proves that the activation committed.
    [Fact]
    public void SendInvitation_EmailSentBeforeTheActivationArrives_CompletesTheOnboarding()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(EmailSent());

        onboarding.State.ShouldBe(TenantOnboardingState.Completed);
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
    public void SendInvitation_FailsForGoodBeforeTheActivationArrives_NeedsAttentionAndCancelsTheInvitation()
    {
        var onboarding = Activating();

        var sent = onboarding.Handle(FaultOf(InvitationReady()));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new CancelInvitation(TenantId, InvitationId), new RaiseOnboardingAlarm(TenantId)]);
    }

    [Fact]
    public void CancelTenant_TenantCancelled_EndsCancelled()
    {
        var onboarding = Registering();
        onboarding.Handle(new RegistrationTimedOut(TenantId, Timeouts.Registration));

        var sent = onboarding.Handle(new TenantCancelled(TenantId));

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelled);
        sent.ShouldBeEmpty();
    }

    // The provider's call may succeed after the registration timed out; what it created is undone.
    [Fact]
    public void RegisterOwner_OwnerRegisteredAfterTheTimeout_RevokesTheRegistration()
    {
        var onboarding = Registering();
        onboarding.Handle(new RegistrationTimedOut(TenantId, Timeouts.Registration));

        var sent = onboarding.Handle(Registered());

        onboarding.State.ShouldBe(TenantOnboardingState.Cancelling);
        sent.ShouldBe([new RevokeOwnerRegistration(TenantId, ProviderInvitationId)]);
    }

    // S9: a compensation that fails for good leaves the process to a person.
    [Fact]
    public void CancelTenant_FailsForGood_NeedsAttention()
    {
        var onboarding = Registering();
        onboarding.Handle(new RegistrationTimedOut(TenantId, Timeouts.Registration));

        var sent = onboarding.Handle(FaultOf(new CancelTenant(TenantId, InvitationId, "registration_timed_out")));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    [Fact]
    public void RevokeOwnerRegistration_FailsForGood_NeedsAttention()
    {
        var onboarding = Activating();
        onboarding.Handle(FaultOf(new ActivateTenant(TenantId, InvitationId, SignUpLink)));

        var sent = onboarding.Handle(FaultOf(new RevokeOwnerRegistration(TenantId, ProviderInvitationId)));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    [Fact]
    public void CancelInvitation_FailsForGood_RaisesTheAlarmAgain()
    {
        var onboarding = SendingInvitation();
        onboarding.Handle(FaultOf(InvitationReady()));

        var sent = onboarding.Handle(FaultOf(new CancelInvitation(TenantId, InvitationId)));

        onboarding.State.ShouldBe(TenantOnboardingState.NeedsAttention);
        sent.ShouldBe([new RaiseOnboardingAlarm(TenantId)]);
    }

    private static TenantOnboarding Registering() =>
        TenantOnboarding.Begin(TenantId, InvitationId, OwnerEmail, AcceptLink, Timeouts).Onboarding;

    private static TenantOnboarding Activating()
    {
        var onboarding = Registering();
        onboarding.Handle(Registered());
        return onboarding;
    }

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

    private static OwnerRegistered Registered() => new(TenantId, InvitationId, SignUpLink, ProviderInvitationId);

    private static TenantActivated Activated() => new(EventId, Now, TenantId, "Acme Ltd", AdminId);

    private static OwnerInvitationReady InvitationReady() => new(EventId, Now, TenantId, InvitationId, OwnerEmail, "Acme Ltd", SignUpLink);

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
