using Api.Application;
using Xunit;

namespace Api.Tests.Unit.Application;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessTrue_ErrorIsNone()
    {
        // Act
        var result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailureTrue_CarriesError()
    {
        // Arrange
        var error = new Error("Some.Code", "Some message");

        // Act
        var result = Result.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericSuccess_ValueAccessible()
    {
        // Act
        var result = Result.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_AccessingValueThrows()
    {
        // Arrange
        var result = Result.Failure<int>(new Error("Code", "Message"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        // Act
        Result<string> result = "hello";

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }
}
