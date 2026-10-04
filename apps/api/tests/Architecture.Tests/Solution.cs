using System.Reflection;
using System.Text.RegularExpressions;

namespace Architecture.Tests;

internal static partial class Solution
{
    public const string SharedKernel = "SharedKernel";
    public const string Host = "Api";

    public static IReadOnlyList<string> Layers { get; } = ["Contracts", "Domain", "Application", "Infrastructure", "Api"];

    // The test project references only the host, so the assemblies the runtime trusts are exactly those the host ships;
    // stale files left in the output directory are not among them.
    private static IReadOnlyList<string> AssemblyFiles { get; } =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);

    public static IReadOnlyList<string> Modules { get; } =
    [
        .. AssemblyFiles
            .Select(file => ModuleAssemblyFileName().Match(Path.GetFileName(file)))
            .Where(match => match.Success)
            .Select(match => match.Groups["module"].Value)
            .Distinct()
            .Order(),
    ];

    public static IReadOnlyList<string> ModuleProjects { get; } = [.. Modules.SelectMany(ProjectsOf)];

    public static IReadOnlyList<string> AllProjects { get; } = [SharedKernel, Host, .. ModuleProjects];

    public static IEnumerable<string> ProjectsOf(string module) => Layers.Select(layer => $"{module}.{layer}");

    public static IEnumerable<string> LayerOfEveryModule(string layer) => Modules.Select(module => $"{module}.{layer}");

    public static bool IsShipped(string project) => AssemblyFiles.Any(file => Path.GetFileName(file) == $"{project}.dll");

    public static Assembly Load(string project) => Assembly.Load(project);

    [GeneratedRegex(@"^(?<module>[A-Za-z]+)\.(Contracts|Domain|Application|Infrastructure|Api)\.dll$")]
    private static partial Regex ModuleAssemblyFileName();
}
