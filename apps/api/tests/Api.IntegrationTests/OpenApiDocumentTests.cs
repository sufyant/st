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
    public void A_result_with_a_value_is_described_as_the_value()
    {
        var success = Responses("/v1/greetings", "post").GetProperty("200");

        success.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/Greeting");
    }

    [Fact]
    public void A_result_without_a_value_is_described_as_no_content()
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
    public void The_failures_of_a_result_are_described_as_problem_details(string status)
    {
        var failure = Responses("/v1/greetings", "post").GetProperty(status);

        failure.GetProperty("content").EnumerateObject().Select(content => content.Name).ShouldBe(["application/problem+json"]);
    }

    [Fact]
    public void The_result_type_itself_is_not_part_of_the_document()
    {
        var schemas = _document.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name);

        schemas.ShouldNotContain(name => name.StartsWith("Result", StringComparison.Ordinal));
    }

    private JsonElement Responses(string path, string method) =>
        _document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");
}
