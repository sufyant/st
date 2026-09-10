using Application.Abstractions;
using Application.Behaviors;
using Application.Results;
using Domain.Access;
using Xunit;

namespace UnitTests.Behaviors;

public sealed record UnguardedCommand : ICommand<Unit>;

[RequiresPermission("members.manage")]
public sealed record GuardedCommand : ICommand<Unit>;

public sealed class StubCurrentUser(params string[] permissions) : ICurrentUser
{
    public ExternalUserId Id { get; } = ExternalUserId.Create("user_1");

    public bool HasPermission(string code) => permissions.Contains(code);

    public bool TryGetEmail(out EmailAddress email)
    {
        email = null!;

        return false;
    }
}

public sealed class PermissionBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithNoAttribute_ReachesTheHandler()
    {
        // Arrange
        var behavior = new PermissionBehavior<UnguardedCommand, Unit>(new StubCurrentUser());

        // Act
        var result = await behavior.HandleAsync(
            new UnguardedCommand(),
            () => Task.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WithoutThePermission_NeverReachesTheHandler()
    {
        // Arrange
        var reached = false;
        var behavior = new PermissionBehavior<GuardedCommand, Unit>(new StubCurrentUser("members.read"));

        // Act
        var result = await behavior.HandleAsync(
            new GuardedCommand(),
            () =>
            {
                reached = true;

                return Task.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reached);
        Assert.Equal(ErrorKind.Forbidden, result.Error.Kind);
    }

    [Fact]
    public async Task HandleAsync_WithThePermission_ReachesTheHandler()
    {
        // Arrange
        var behavior = new PermissionBehavior<GuardedCommand, Unit>(new StubCurrentUser("members.manage"));

        // Act
        var result = await behavior.HandleAsync(
            new GuardedCommand(),
            () => Task.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
    }
}
