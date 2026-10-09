using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Api.IntegrationTests;

public sealed class ProblemDetailsTests : IAsyncLifetime
{
    private PipelineHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await PipelineHost.StartAsync();
        _client = _host.CreateClient();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task AnswerResult_SuccessWithAValue_ReturnsTheValue()
    {
        var response = await _client.PostAsJsonAsync("/v1/greetings", new { name = "Ada" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var greeting = await response.Content.ReadFromJsonAsync<Greeting>(TestContext.Current.CancellationToken);
        greeting.ShouldBe(new Greeting("Hello, Ada"));
    }

    [Fact]
    public async Task AnswerResult_SuccessWithoutAValue_ReturnsNoContent()
    {
        var response = await _client.PostAsJsonAsync("/v1/acknowledgements", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("Validation", HttpStatusCode.BadRequest)]
    [InlineData("NotFound", HttpStatusCode.NotFound)]
    [InlineData("Conflict", HttpStatusCode.Conflict)]
    [InlineData("Forbidden", HttpStatusCode.Forbidden)]
    [InlineData("Unprocessable", HttpStatusCode.UnprocessableEntity)]
    public async Task AnswerResult_ExpectedFailure_ReturnsProblemDetailsWithItsStatusAndCode(string errorType, HttpStatusCode status)
    {
        var response = await _client.PostAsync($"/v1/failures/{errorType}", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Status.ShouldBe((int)status);
        problem.Detail.ShouldBe("The test asked for a failure.");
        problem.Extensions["code"]!.ToString().ShouldBe("test.failure");
        problem.Extensions.ShouldContainKey("traceId");
    }

    [Fact]
    public async Task AnswerResult_InvalidCommand_ReturnsValidationProblemDetailsListingTheFields()
    {
        var response = await _client.PostAsJsonAsync("/v1/greetings", new { name = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Errors.Keys.ShouldBe(["Name"]);
    }

    [Fact]
    public async Task AnswerResult_UnexpectedException_ReturnsAGenericServerError()
    {
        var response = await _client.PostAsync("/v1/explosions", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Status.ShouldBe(500);
        problem.Extensions.ShouldContainKey("traceId");
    }

    [Fact]
    public async Task AnswerResult_UnexpectedException_LeaksNoMessageOrStackTrace()
    {
        var response = await _client.PostAsync("/v1/explosions", null, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("hunter2");
        body.ShouldNotContain(nameof(InvalidOperationException));
        body.ShouldNotContain("ExplodeHandler");
    }
}
