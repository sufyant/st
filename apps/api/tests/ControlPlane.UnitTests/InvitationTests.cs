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

    // The token is born when the invitation is delivered, so no stored message ever carries it (0029).
    [Fact]
    public void A_new_invitation_has_no_token_yet()
    {
        var invitation = Invite("ada@example.com");

        invitation.TokenHash.ShouldBeNull();
    }

    [Fact]
    public void Issuing_a_token_stores_its_hash_never_the_token()
    {
        var invitation = Invite("ada@example.com");

        var issued = invitation.IssueToken(Token);

        issued.ShouldBeTrue();
        invitation.TokenHash.ShouldBe(InvitationToken.Hash(Token));
        invitation.TokenHash.ShouldNotBeNull().ShouldNotContain(Token);
    }

    // A delivery that arrives twice must not replace the token the first one sent.
    [Fact]
    public void A_token_is_issued_only_once()
    {
        var invitation = Invite("ada@example.com");
        invitation.IssueToken(Token);

        var issued = invitation.IssueToken("another-token");

        issued.ShouldBeFalse();
        invitation.TokenHash.ShouldBe(InvitationToken.Hash(Token));
    }

    [Fact]
    public void An_invitation_that_is_no_longer_pending_is_issued_no_token()
    {
        var invitation = Invite("ada@example.com");
        invitation.Accept(["ada@example.com"], AccepterId, Now);

        var issued = invitation.IssueToken(Token);

        issued.ShouldBeFalse();
        invitation.TokenHash.ShouldBeNull();
    }

    [Fact]
    public void A_new_invitation_is_pending_until_its_lifetime_ends()
    {
        var invitation = Invite("ada@example.com");

        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_invitation_is_accepted_by_a_user_whose_verified_email_matches()
    {
        var invitation = Invite("ada@example.com");

        var accepted = invitation.Accept(["other@example.com", "ada@example.com"], AccepterId, Now.AddDays(1));

        accepted.IsSuccess.ShouldBeTrue();
        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedBy.ShouldBe(AccepterId);
    }

    [Fact]
    public void Matching_the_email_ignores_case()
    {
        var invitation = Invite("Ada@Example.com");

        var accepted = invitation.Accept(["ada@example.com"], AccepterId, Now);

        accepted.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_invitation_cannot_be_accepted_twice()
    {
        var invitation = Invite("ada@example.com");
        invitation.Accept(["ada@example.com"], AccepterId, Now);

        var second = invitation.Accept(["ada@example.com"], AccepterId, Now);

        second.Error.Code.ShouldBe("invitation.not_pending");
    }

    [Fact]
    public void An_invitation_cannot_be_accepted_once_its_lifetime_has_ended()
    {
        var invitation = Invite("ada@example.com");

        var accepted = invitation.Accept(["ada@example.com"], AccepterId, Now.AddDays(7));

        accepted.Error.Code.ShouldBe("invitation.expired");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    // The token alone is not enough: a forwarded link does not let someone else in (0029).
    [Fact]
    public void An_invitation_cannot_be_accepted_by_a_user_without_the_invited_email()
    {
        var invitation = Invite("ada@example.com");

        var accepted = invitation.Accept(["grace@example.com"], AccepterId, Now);

        accepted.Error.Code.ShouldBe("invitation.email_mismatch");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    private static Invitation Invite(string email) =>
        Invitation.Create(Id, TenantId, email, BuiltInRoles.Member, InviterId, Now, Lifetime);
}
