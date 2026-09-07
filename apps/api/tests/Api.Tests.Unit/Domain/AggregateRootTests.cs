using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class AggregateRootTests
{
    private sealed record TestDomainEvent(string Reason) : DomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate(Guid id) : base(id)
        {
        }

        public void DoSomething(string reason) => Raise(new TestDomainEvent(reason));
    }

    [Fact]
    public void Raise_AddsEventToDomainEvents()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        aggregate.DoSomething("first");

        // Assert
        Assert.Single(aggregate.DomainEvents);
        Assert.Equal("first", Assert.IsType<TestDomainEvent>(aggregate.DomainEvents[0]).Reason);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.DoSomething("first");
        aggregate.DoSomething("second");

        // Act
        aggregate.ClearDomainEvents();

        // Assert
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvent_OccurredOnUtc_IsSetToUtcNowAtCreation()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var domainEvent = new TestDomainEvent("reason");
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.InRange(domainEvent.OccurredOnUtc, before, after);
    }
}
