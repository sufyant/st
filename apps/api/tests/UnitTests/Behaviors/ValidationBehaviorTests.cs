using Application.Abstractions;
using Application.Behaviors;
using Application.Results;
using FluentValidation;
using Xunit;

namespace UnitTests.Behaviors;

public sealed record InviteCommand(string Email, string RoleCode) : ICommand<Unit>;

public sealed class InviteCommandValidator : AbstractValidator<InviteCommand>
{
    public InviteCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty();
        RuleFor(command => command.RoleCode).NotEmpty();
    }
}

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithNoValidator_ReachesTheHandler()
    {
        // Arrange
        var reached = false;
        var behavior = new ValidationBehavior<InviteCommand, Unit>([]);

        // Act
        var result = await behavior.HandleAsync(
            new InviteCommand("", ""),
            () =>
            {
                reached = true;

                return Task.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reached);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WithAnInvalidRequest_NeverReachesTheHandler()
    {
        // Arrange
        var reached = false;
        var behavior = new ValidationBehavior<InviteCommand, Unit>([new InviteCommandValidator()]);

        // Act
        var result = await behavior.HandleAsync(
            new InviteCommand("", ""),
            () =>
            {
                reached = true;

                return Task.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reached);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public async Task HandleAsync_CollectsEveryFieldFailureIntoOneError()
    {
        // Arrange
        var behavior = new ValidationBehavior<InviteCommand, Unit>([new InviteCommandValidator()]);

        // Act
        var result = await behavior.HandleAsync(
            new InviteCommand("", ""),
            () => Task.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Email", "RoleCode"],
            result.Error.Failures.Keys.OrderBy(key => key));
    }

    [Fact]
    public async Task HandleAsync_WithAValidRequest_ReachesTheHandler()
    {
        // Arrange
        var behavior = new ValidationBehavior<InviteCommand, Unit>([new InviteCommandValidator()]);

        // Act
        var result = await behavior.HandleAsync(
            new InviteCommand("someone@example.com", "member"),
            () => Task.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
    }
}
