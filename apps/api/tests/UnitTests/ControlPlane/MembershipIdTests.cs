using Domain.ControlPlane.Memberships;
using Xunit;

namespace UnitTests.ControlPlane;

public sealed class MembershipIdTests
{
    [Fact]
    public void From_ForAnEmptyGuid_Throws()
    {
        // Arrange
        var value = Guid.Empty;

        // Act
        Func<object?> act = () => MembershipId.From(value);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
