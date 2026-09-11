using Domain.Shared;
using Xunit;

namespace UnitTests.Shared;

public sealed class EntityTests
{
    private sealed class Cat : Entity<Guid>
    {
        public Cat(Guid id) => Id = id;
    }

    private sealed class Dog : Entity<Guid>
    {
        public Dog(Guid id) => Id = id;
    }

    [Fact]
    public void TwoInstancesWithTheSameIdentityAreEqual()
    {
        // Arrange
        var id = Guid.CreateVersion7();

        // Act
        var left = new Cat(id);
        var right = new Cat(id);

        // Assert
        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void TheSameIdentityInADifferentTypeIsNotEqual()
    {
        // Arrange
        var id = Guid.CreateVersion7();

        // Act
        var cat = new Cat(id);
        var dog = new Dog(id);

        // Assert
        Assert.NotEqual<object>(cat, dog);
    }

    [Fact]
    public void ComparingWithNullIsSafe()
    {
        // Arrange
        Cat? absent = null;

        // Act
        var cat = new Cat(Guid.CreateVersion7());

        // Assert
        Assert.False(cat == absent);
        Assert.True(cat != absent);
        Assert.True(absent == null);
    }
}
