using Infrastructure.Messaging;
using Xunit;

namespace IntegrationTests;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsUnprocessedAndImmediatelyDue()
    {
        // Arrange & Act
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Assert
        Assert.Null(message.ProcessedAt);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(Now, message.NextAttemptAt);
    }

    [Fact]
    public void RecordFailure_CountsTheAttemptAndBacksOff()
    {
        // Arrange
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Act
        message.RecordFailure("boom", Now);

        // Assert
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("boom", message.LastError);
        Assert.Equal(Now.AddSeconds(10), message.NextAttemptAt);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public void RecordFailure_BacksOffFurtherOnEachAttempt()
    {
        // Arrange
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Act
        message.RecordFailure("boom", Now);
        message.RecordFailure("boom", Now);
        message.RecordFailure("boom", Now);

        // Assert
        Assert.Equal(3, message.AttemptCount);
        Assert.Equal(Now.AddSeconds(40), message.NextAttemptAt);
    }

    [Fact]
    public void MarkProcessed_StampsTheCompletionTime()
    {
        // Arrange
        var message = OutboxMessage.Create(Guid.NewGuid(), "Type", "{}", Now);

        // Act
        message.MarkProcessed(Now.AddSeconds(5));

        // Assert
        Assert.Equal(Now.AddSeconds(5), message.ProcessedAt);
    }
}
