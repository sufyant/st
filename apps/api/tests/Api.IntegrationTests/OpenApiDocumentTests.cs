using System.Net.Http.Json;
using System.Text.Json;

namespace Api.IntegrationTests;

// Endpoints return the Result of their command and the host maps it, so the document must describe what the client
// actually receives, not the Result type.
public sealed class OpenApiDocumentTests : IAsyncLifetime
{
    private PipelineHost _host = null!;
    private JsonElement _document;

    public async ValueTask InitializeAsync()
    {
        _host = await PipelineHost.StartAsync();
        _document = await _host.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public void DescribeEndpoint_ResultWithAValue_IsTheValue()
    {
        var success = Responses("/v1/greetings", "post").GetProperty("200");

        success.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/Greeting");
    }

    [Fact]
    public void DescribeEndpoint_ResultWithoutAValue_IsNoContent()
    {
        var responses = Responses("/v1/acknowledgements", "post");

        responses.EnumerateObject().Select(response => response.Name).ShouldContain("204");
        responses.EnumerateObject().Select(response => response.Name).ShouldNotContain("200");
    }

    [Theory]
    [InlineData("400")]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("409")]
    [InlineData("422")]
    public void DescribeEndpoint_FailuresOfAResult_AreProblemDetails(string status)
    {
        var failure = Responses("/v1/greetings", "post").GetProperty(status);

        failure.GetProperty("content").EnumerateObject().Select(content => content.Name).ShouldBe(["application/problem+json"]);
    }

    [Fact]
    public void DescribeEndpoint_ResultType_IsNotInTheDocument()
    {
        var schemas = _document.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name);

        schemas.ShouldNotContain(name => name.StartsWith("Result", StringComparison.Ordinal));
    }

    private JsonElement Responses(string path, string method) =>
        _document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");
}
