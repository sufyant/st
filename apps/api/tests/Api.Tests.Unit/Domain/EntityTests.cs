using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class EntityTests
{
    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id) : base(id) { }
    }

    private sealed class OtherEntity : Entity<Guid>
    {
        public OtherEntity(Guid id) : base(id) { }
    }

    [Fact]
    public void Equals_SameTypeAndId_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var first = new TestEntity(id);
        var second = new TestEntity(id);

        // Act
        var areEqual = first.Equals(second);

        // Assert
        Assert.True(areEqual);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        // Arrange
        var first = new TestEntity(Guid.NewGuid());
        var second = new TestEntity(Guid.NewGuid());

        // Act & Assert
        Assert.False(first.Equals(second));
    }

    [Fact]
    public void Equals_SameIdDifferentType_ReturnsFalse()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new TestEntity(id);
        var other = new OtherEntity(id);

        // Act & Assert
        Assert.False(entity.Equals(other));
    }

    [Fact]
    public void EqualityOperator_BothNull_ReturnsTrue()
    {
        // Arrange
        TestEntity? left = null;
        TestEntity? right = null;

        // Act & Assert
        Assert.True(left == right);
    }
}
