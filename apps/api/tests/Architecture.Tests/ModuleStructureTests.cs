using System.Runtime.CompilerServices;

namespace Architecture.Tests;

public class ModuleStructureTests
{
    public static TheoryData<string> Modules => [.. Solution.Modules];

    // The host is left out: no project can reference it without a cycle, and its top-level Program has no namespace.
    public static TheoryData<string> NamespacedProjects => [Solution.SharedKernel, Solution.Tenancy, .. Solution.ModuleProjects];

    [Fact]
    public void The_template_modules_are_found()
    {
        string[] templateModules = ["Audit", "ControlPlane", "Notifications"];

        templateModules.ShouldBeSubsetOf(Solution.Modules);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Every_module_has_all_five_projects(string module)
    {
        var missing = Solution.ProjectsOf(module).Where(project => !Solution.IsShipped(project));

        missing.ShouldBeEmpty();
    }

    // Dependency rules match on namespaces, so a type outside its project's namespace would escape them.
    [Theory]
    [MemberData(nameof(NamespacedProjects))]
    public void Types_live_under_their_project_namespace(string project)
    {
        var strays = TypesOutsideTheNamespaceOf(project);

        strays.ShouldBeEmpty();
    }

    private static IEnumerable<string?> TypesOutsideTheNamespaceOf(string project) =>
        Solution.Load(project).GetTypes()
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(type => type.Namespace != project
                && type.Namespace?.StartsWith($"{project}.", StringComparison.Ordinal) != true)
            .Select(type => type.FullName);
}
