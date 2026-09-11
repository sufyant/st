using Domain.Authorization;
using Xunit;

namespace UnitTests.Authorization;

public sealed class OwnerRosterTests
{
    [Fact]
    public void IsLastOwner_ForTheOnlyOwner_IsTrue()
    {
        // Arrange
        var owner = Guid.CreateVersion7();
        var roster = OwnerRoster.Of([owner]);

        // Act
        var isLast = roster.IsLastOwner(owner);

        // Assert
        Assert.True(isLast);
    }

    [Fact]
    public void IsLastOwner_WithASecondOwner_IsFalse()
    {
        // Arrange
        var owner = Guid.CreateVersion7();
        var roster = OwnerRoster.Of([owner, Guid.CreateVersion7()]);

        // Act
        var isLast = roster.IsLastOwner(owner);

        // Assert
        Assert.False(isLast);
    }

    [Fact]
    public void IsLastOwner_ForSomeoneWhoIsNotAnOwner_IsFalse()
    {
        // Arrange
        var roster = OwnerRoster.Of([Guid.CreateVersion7()]);

        // Act
        var isLast = roster.IsLastOwner(Guid.CreateVersion7());

        // Assert
        Assert.False(isLast);
    }

    [Fact]
    public void IsLastOwner_WithNoOwners_IsFalse()
    {
        // Arrange
        var roster = OwnerRoster.Of([]);

        // Act
        var isLast = roster.IsLastOwner(Guid.CreateVersion7());

        // Assert
        Assert.False(isLast);
    }
}
