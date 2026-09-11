using Domain.ControlPlane.Invitations;
using Xunit;

namespace UnitTests.ControlPlane;

public sealed class InvitationIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Func<object?> act = () => InvitationId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
