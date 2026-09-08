using Api.Application;
using Api.Host;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace Api.Tests.Unit.Host;

public class ResultHttpMapperTests
{
    [Fact]
    public void ToHttpResult_UnmappedErrorCode_ReturnsGenericDetailAndLogsError()
    {
        // Arrange
        var logger = new CapturingLogger();
        var error = new Error("Some.Unmapped.Code", "Internal detail that must not reach clients");
        var result = Result.Failure(error);

        // Act
        var httpResult = result.ToHttpResult(logger);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemResult.StatusCode);
        Assert.Equal("An unexpected error occurred.", problemResult.ProblemDetails.Detail);
        Assert.True(logger.ErrorLogged);
    }

    [Fact]
    public void ToHttpResult_KnownErrorCode_ReturnsErrorMessageVerbatimAndDoesNotLog()
    {
        // Arrange
        var logger = new CapturingLogger();
        var error = new Error("Validation.Required", "Name is required");
        var result = Result.Failure(error);

        // Act
        var httpResult = result.ToHttpResult(logger);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
        Assert.Equal("Name is required", problemResult.ProblemDetails.Detail);
        Assert.False(logger.ErrorLogged);
    }

    private sealed class CapturingLogger : ILogger
    {
        public bool ErrorLogged { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                ErrorLogged = true;
            }
        }
    }
}
