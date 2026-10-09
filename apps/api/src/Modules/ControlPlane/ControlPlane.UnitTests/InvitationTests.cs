using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;

namespace ControlPlane.UnitTests;

public class InvitationTests
{
    private static readonly Guid Id = new("0199a8f0-0000-7000-8000-000000000401");
    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");
    private static readonly Guid InviterId = new("0199a8f0-0000-7000-8000-000000000301");
    private static readonly Guid AccepterId = new("0199a8f0-0000-7000-8000-000000000302");
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private const string Token = "the-invitation-token";

    // The secret is born with the invitation, and only its hash is kept.
    [Fact]
    public void CreateInvitation_WithItsSecret_StoresTheHashNeverTheSecret()
    {
        var invitation = Invite("ada@example.com");

        invitation.TokenHash.ShouldBe(InvitationToken.Hash(Token));
        invitation.TokenHash.ShouldNotBeNull().ShouldNotContain(Token);
    }

    [Fact]
    public void CancelInvitation_Pending_IsCancelled()
    {
        var invitation = Invite("ada@example.com");

        var cancelled = invitation.Cancel();

        cancelled.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
    }

    // A compensation may arrive twice.
    [Fact]
    public void CancelInvitation_AlreadyCancelled_ChangesNothing()
    {
        var invitation = Invite("ada@example.com");
        invitation.Cancel();

        var cancelled = invitation.Cancel();

        cancelled.ShouldBeFalse();
        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
    }

    [Fact]
    public void CancelInvitation_Accepted_StaysAccepted()
    {
        var invitation = Invite("ada@example.com");
        invitation.Accept(["ada@example.com"], AccepterId, Now);

        var cancelled = invitation.Cancel();

        cancelled.ShouldBeFalse();
        invitation.Status.ShouldBe(InvitationStatus.Accepted);
    }

    [Fact]
    public void AcceptInvitation_Cancelled_IsRefused()
    {
        var invitation = Invite("ada@example.com");
        invitation.Cancel();

        var accepted = invitation.Accept(["ada@example.com"], AccepterId, Now);

        accepted.Error.Code.ShouldBe("invitation.not_pending");
        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
    }

    [Fact]
    public void InviteOwner_NewInvitation_IsPendingUntilItsLifetimeEnds()
    {
        var invitation = Invite("ada@example.com");

        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AcceptInvitation_VerifiedEmailMatches_IsAccepted()
    {
        var invitation = Invite("ada@example.com");

        var accepted = invitation.Accept(["other@example.com", "ada@example.com"], AccepterId, Now.AddDays(1));

        accepted.IsSuccess.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedBy.ShouldBe(AccepterId);
    }

    [Fact]
    public void AcceptInvitation_EmailInAnotherCase_IsAccepted()
    {
        var invitation = Invite("Ada@Example.com");

        var accepted = invitation.Accept(["ada@example.com"], AccepterId, Now);

        accepted.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AcceptInvitation_AlreadyAccepted_IsRejected()
    {
        var invitation = Invite("ada@example.com");
        invitation.Accept(["ada@example.com"], AccepterId, Now);

        var second = invitation.Accept(["ada@example.com"], AccepterId, Now);

        second.Error.Code.ShouldBe("invitation.not_pending");
    }

    [Fact]
    public void AcceptInvitation_AfterItsLifetime_IsRejectedAsExpired()
    {
        var invitation = Invite("ada@example.com");

        var accepted = invitation.Accept(["ada@example.com"], AccepterId, Now.AddDays(7));

        accepted.Error.Code.ShouldBe("invitation.expired");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    // The token alone is not enough: a forwarded link does not let someone else in.
    [Fact]
    public void AcceptInvitation_WithoutTheInvitedEmail_IsRejected()
    {
        var invitation = Invite("ada@example.com");

        var accepted = invitation.Accept(["grace@example.com"], AccepterId, Now);

        accepted.Error.Code.ShouldBe("invitation.email_mismatch");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    private static Invitation Invite(string email) =>
        Invitation.Create(Id, TenantId, email, BuiltInRoles.Member, InviterId, Now, Lifetime, Token);
}
