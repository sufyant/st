using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Architecture.Tests;

internal static partial class Solution
{
    public const string SharedKernel = "SharedKernel";
    public const string Tenancy = "Tenancy";
    public const string Host = "Api";

    private const string SolutionFile = "Api.slnx";

    public static IReadOnlyList<string> Layers { get; } = ["Contracts", "Domain", "Application", "Infrastructure", "Api"];

    // A test project is known by its name, not by its folder: module tests sit next to their module (section 2).
    public static IReadOnlyList<string> TestProjectSuffixes { get; } = [".UnitTests", ".IntegrationTests", ".EndToEndTests", ".Tests"];

    // The deps file marks the solution's own projects apart from third-party packages, some of which follow the module
    // naming pattern too (OpenTelemetry.Api), and lists each project's direct project references. The test project references
    // the host and the other test projects, so without the test projects these are exactly the projects the host ships.
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectReferences { get; } = ReadProjectReferencesFromDepsFile();

    private static IReadOnlyList<string> ProjectNames { get; } = [.. ProjectReferences.Keys];

    // The project files themselves, as the solution file lists them.
    private static Dictionary<string, string> ProjectFiles { get; } = ReadProjectFilesFromSolution();

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

    public static IReadOnlyList<string> AllProjects { get; } = [SharedKernel, Tenancy, Host, .. ModuleProjects];

    // Every project the solution file lists, test projects and production projects.
    public static IReadOnlyList<string> TestProjects { get; } = [.. ProjectFiles.Keys.Where(IsTestProject).Order()];

    public static IReadOnlyList<string> ProductionProjects { get; } = [.. ProjectFiles.Keys.Where(project => !IsTestProject(project)).Order()];

    public static bool IsTestProject(string project) =>
        TestProjectSuffixes.Any(suffix => project.EndsWith(suffix, StringComparison.Ordinal));

    public static IEnumerable<string> ProjectsOf(string module) => Layers.Select(layer => $"{module}.{layer}");

    public static IEnumerable<string> LayerOfEveryModule(string layer) => Modules.Select(module => $"{module}.{layer}");

    public static bool IsShipped(string project) => ProjectNames.Contains(project);

    public static Assembly Load(string project) => Assembly.Load(project);

    // What the project file itself declares: the projects, packages and shared frameworks it references.
    public static ProjectFile ReadProjectFile(string project)
    {
        var file = XDocument.Load(ProjectFiles[project]);

        return new ProjectFile(
            [.. Includes(file, "ProjectReference").Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))],
            [.. Includes(file, "PackageReference")],
            [.. Includes(file, "FrameworkReference")]);
    }

    // The C# files of a project: every file under its folder, without the build output.
    public static IEnumerable<string> SourceFilesOf(string project)
    {
        var folder = Path.GetDirectoryName(ProjectFiles[project])!;
        string[] buildOutput = [$"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"];

        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !buildOutput.Any(output => file[folder.Length..].Contains(output, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Includes(XDocument file, string item) =>
        file.Descendants(item).Select(element => (string)element.Attribute("Include")!);

    // The solution file is found above the test's output directory, in the source tree the test was built from.
    private static Dictionary<string, string> ReadProjectFilesFromSolution()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, SolutionFile)))
        {
            root = root.Parent ?? throw new InvalidOperationException($"{SolutionFile} was not found above the test's output directory.");
        }

        return XDocument.Load(Path.Combine(root.FullName, SolutionFile)).Descendants("Project")
            .Select(project => Path.Combine(root.FullName, (string)project.Attribute("Path")!))
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, path => path);
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadProjectReferencesFromDepsFile()
    {
        // The runtime lists the application's own deps file first, separated by semicolons on every platform.
        var depsFile = ((string)AppContext.GetData("APP_CONTEXT_DEPS_FILES")!).Split(';')[0];
        using var deps = JsonDocument.Parse(File.ReadAllText(depsFile));

        var projects = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "project")
            .Select(library => library.Name)
            .Where(library => !IsTestProject(NameOf(library)))
            .ToList();
        var targets = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        var names = projects.Select(NameOf).ToHashSet();

        return projects.ToDictionary(
            NameOf,
            IReadOnlyList<string> (library) => targets.GetProperty(library).TryGetProperty("dependencies", out var dependencies)
                ? [.. dependencies.EnumerateObject().Select(dependency => dependency.Name).Where(names.Contains)]
                : []);
    }

    private static string NameOf(string library) => library.Split('/')[0];

    [GeneratedRegex(@"^(?<module>[A-Za-z]+)\.(Contracts|Domain|Application|Infrastructure|Api)$")]
    private static partial Regex ModuleProjectName();
}

internal sealed record ProjectFile(
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences);
