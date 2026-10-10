using System.Runtime.CompilerServices;

namespace Architecture.Tests;

public class ModuleStructureTests
{
    public static TheoryData<string> Modules => [.. Solution.Modules];

    // The host is left out: no project can reference it without a cycle, and its top-level Program has no namespace.
    public static TheoryData<string> NamespacedProjects => [Solution.SharedKernel, Solution.Tenancy, .. Solution.ModuleProjects];

    [Fact]
    public void FindModules_InTheSolution_IncludesTheTemplateModules()
    {
        string[] templateModules = ["Audit", "ControlPlane", "Notifications"];

        templateModules.ShouldBeSubsetOf(Solution.Modules);
    }

    // A module project that nothing references is still a module, so the rules check it too (section 2).
    [Fact]
    public void FindModules_InTheSolutionFile_IncludesEveryModuleItLists()
    {
        var listed = Solution.SolutionProjects
            .Where(project => Solution.Layers.Any(layer => project.EndsWith($".{layer}", StringComparison.Ordinal)))
            .Select(project => project[..project.IndexOf('.', StringComparison.Ordinal)]);

        listed.ShouldBeSubsetOf(Solution.Modules);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ShipModule_AnyModule_HasAllFiveProjects(string module)
    {
        var missing = Solution.ProjectsOf(module).Where(project => !Solution.IsShipped(project));

        missing.ShouldBeEmpty();
    }

    // Dependency rules match on namespaces, so a type outside its project's namespace would escape them.
    [Theory]
    [MemberData(nameof(NamespacedProjects))]
    public void PlaceTypes_InAProject_LiveUnderItsNamespace(string project)
    {
        var strays = TypesOutsideTheNamespaceOf(project);

        strays.ShouldBeEmpty();
    }

    // The compiler places types it synthesizes, such as those behind collection expressions, in the global namespace. Only it can
    // give a type a name starting with '<', so those are left out; every type written in source is still checked.
    private static IEnumerable<string?> TypesOutsideTheNamespaceOf(string project) =>
        Solution.Load(project).GetTypes()
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(type => !OutermostType(type).Name.StartsWith('<'))
            .Where(type => type.Namespace != project
                && type.Namespace?.StartsWith($"{project}.", StringComparison.Ordinal) != true)
            .Select(type => type.FullName);

    private static Type OutermostType(Type type) => type.DeclaringType is null ? type : OutermostType(type.DeclaringType);
}
