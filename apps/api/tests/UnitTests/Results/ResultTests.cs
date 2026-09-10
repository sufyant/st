using Application.Results;
using Xunit;

namespace UnitTests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_ExposesTheValue()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var value = result.Value;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, value);
    }

    [Fact]
    public void Success_RefusesToExposeAnError()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var act = void () => _ = result.Error;

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Failure_ExposesTheError()
    {
        // Arrange
        var error = Error.NotFound("member.missing", "The member does not exist.");
        var result = Result<int>.Failure(error);

        // Act
        var actual = result.Error;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, actual.Kind);
        Assert.Equal("member.missing", actual.Code);
    }

    [Fact]
    public void Failure_RefusesToExposeAValue()
    {
        // Arrange
        var result = Result<int>.Failure(Error.Conflict("alias.taken", "Taken."));

        // Act
        var act = void () => _ = result.Value;

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validation_CarriesFieldFailures()
    {
        // Arrange
        var failures = new Dictionary<string, string[]> { ["Email"] = ["Not an email."] };

        // Act
        var error = Error.Validation(failures);

        // Assert
        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal(["Not an email."], error.Failures["Email"]);
    }

    [Fact]
    public void Failure_CarriesTheErrorAcrossAUnitResult()
    {
        // Arrange
        var error = Error.Forbidden("permission.missing", "Missing 'members.manage'.");

        // Act
        var result = Result<Unit>.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }
}
