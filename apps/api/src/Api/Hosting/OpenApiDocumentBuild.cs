using System.Reflection;
using Microsoft.Extensions.Options;

namespace Api.Hosting;

// The build writes the OpenAPI document with the GetDocument.Insider tool, which runs Program and starts the host without any
// configuration, on a server that serves no request. That process only describes the API: the host registers no database for it and
// checks nothing while it starts (ApiPipeline). This is the one place that tells it apart.
internal static class OpenApiDocumentBuild
{
    public static bool IsRunning { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    // Takes the place of the validator that checks every setting on start (ValidateOnStart).
    public sealed class NothingToCheck : IStartupValidator
    {
        public void Validate()
        {
        }
    }
}
