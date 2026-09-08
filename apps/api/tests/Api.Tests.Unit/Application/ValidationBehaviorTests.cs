using Api.Application;
using FluentValidation;
using Xunit;

namespace Api.Tests.Unit.Application;

public class ValidationBehaviorTests
{
    private sealed record CreateThing(string Name) : IRequest<Result<string>>;

    private sealed class CreateThingValidator : AbstractValidator<CreateThing>
    {
        public CreateThingValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }

    [Fact]
    public async Task Handle_InvalidRequest_ShortCircuitsWithFailureAndNeverCallsNext()
    {
        // Arrange
        var behavior = new ValidationBehavior<CreateThing, Result<string>>([new CreateThingValidator()]);
        var nextCalled = false;
        RequestHandlerDelegate<Result<string>> next = () =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success("unused"));
        };

        // Act
        var result = await behavior.Handle(new CreateThing(""), next, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        // Arrange
        var behavior = new ValidationBehavior<CreateThing, Result<string>>([new CreateThingValidator()]);
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Result.Success("ok"));

        // Act
        var result = await behavior.Handle(new CreateThing("valid"), next, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public async Task Handle_NonGenericResultResponse_ShortCircuitsCorrectly()
    {
        // Arrange
        var behavior = new ValidationBehavior<PlainCommand, Result>([new PlainCommandValidator()]);
        RequestHandlerDelegate<Result> next = () => Task.FromResult(Result.Success());

        // Act
        var result = await behavior.Handle(new PlainCommand(""), next, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
    }

    private sealed record PlainCommand(string Name) : IRequest<Result>;

    private sealed class PlainCommandValidator : AbstractValidator<PlainCommand>
    {
        public PlainCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
