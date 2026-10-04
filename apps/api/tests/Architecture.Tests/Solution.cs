using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Architecture.Tests;

internal static partial class Solution
{
    public const string SharedKernel = "SharedKernel";
    public const string Host = "Api";

    public static IReadOnlyList<string> Layers { get; } = ["Contracts", "Domain", "Application", "Infrastructure", "Api"];

    // The deps file marks the solution's own projects apart from third-party packages, some of which follow the module
    // naming pattern too (OpenTelemetry.Api). The test project references only the host, so these are exactly the projects
    // the host ships.
    private static IReadOnlyList<string> ProjectNames { get; } = ReadProjectNamesFromDepsFile();

    public static IReadOnlyList<string> Modules { get; } =
    [
        .. ProjectNames
            .Select(name => ModuleProjectName().Match(name))
            .Where(match => match.Success)
            .Select(match => match.Groups["module"].Value)
            .Distinct()
            .Order(),
    ];

    public static IReadOnlyList<string> ModuleProjects { get; } = [.. Modules.SelectMany(ProjectsOf)];

    public static IReadOnlyList<string> AllProjects { get; } = [SharedKernel, Host, .. ModuleProjects];

    public static IEnumerable<string> ProjectsOf(string module) => Layers.Select(layer => $"{module}.{layer}");

    public static IEnumerable<string> LayerOfEveryModule(string layer) => Modules.Select(module => $"{module}.{layer}");

    public static bool IsShipped(string project) => ProjectNames.Contains(project);

    public static Assembly Load(string project) => Assembly.Load(project);

    private static string[] ReadProjectNamesFromDepsFile()
    {
        // The runtime lists the application's own deps file first, separated by semicolons on every platform.
        var depsFile = ((string)AppContext.GetData("APP_CONTEXT_DEPS_FILES")!).Split(';')[0];
        using var deps = JsonDocument.Parse(File.ReadAllText(depsFile));

        return
        [
            .. deps.RootElement.GetProperty("libraries").EnumerateObject()
                .Where(library => library.Value.GetProperty("type").GetString() == "project")
                .Select(library => library.Name.Split('/')[0]),
        ];
    }

    [GeneratedRegex(@"^(?<module>[A-Za-z]+)\.(Contracts|Domain|Application|Infrastructure|Api)$")]
    private static partial Regex ModuleProjectName();
}
