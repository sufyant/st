using Domain.ControlPlane.Invitations;
using Domain.ControlPlane.Tenants;
using Domain.Shared;
using Xunit;

namespace UnitTests.ControlPlane;

public sealed class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsPendingWithTheGivenExpiry()
    {
        // Arrange & Act
        var invitation = CreateInvitation();

        // Assert
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.Null(invitation.Acceptance);
    }

    [Fact]
    public void IsExpired_OnlyAfterTheExpiryMoment()
    {
        // Arrange
        var invitation = CreateInvitation();

        // Act & Assert
        Assert.False(invitation.IsExpired(Now.AddDays(7)));
        Assert.True(invitation.IsExpired(Now.AddDays(7).AddSeconds(1)));
    }

    [Fact]
    public void Accept_StampsTheAcceptingUser()
    {
        // Arrange
        var invitation = CreateInvitation();
        var acceptedBy = ExternalUserId.Create("user_invited");

        // Act
        invitation.Accept(acceptedBy, Now.AddHours(1));

        // Assert
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.Equal(acceptedBy, invitation.Acceptance?.By);
        Assert.Equal(Now.AddHours(1), invitation.Acceptance?.At);
    }

    [Fact]
    public void Accept_Twice_Throws()
    {
        // Arrange
        var invitation = CreateInvitation();
        invitation.Accept(ExternalUserId.Create("user_invited"), Now);

        // Act
        var act = () => invitation.Accept(ExternalUserId.Create("user_other"), Now);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Revoke_APendingInvitation_Succeeds()
    {
        // Arrange
        var invitation = CreateInvitation();

        // Act
        invitation.Revoke();

        // Assert
        Assert.Equal(InvitationStatus.Revoked, invitation.Status);
    }

    [Fact]
    public void Revoke_AnAcceptedInvitation_Throws()
    {
        // Arrange
        var invitation = CreateInvitation();
        invitation.Accept(ExternalUserId.Create("user_invited"), Now);

        // Act
        var act = () => invitation.Revoke();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    private static Invitation CreateInvitation() => Invitation.Create(
        TenantId.New(),
        EmailAddress.Create("invited@example.com"),
        "member",
        "token-hash",
        ExternalUserId.Create("user_owner"),
        Now,
        TimeSpan.FromDays(7));
}
