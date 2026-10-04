using System.Runtime.InteropServices;
using Mono.Cecil;
using NetArchTest.Rules;

namespace Architecture.Tests;

public class ReferenceRuleTests
{
    public static TheoryData<string> Modules => [.. Solution.Modules];

    [Fact]
    public void SharedKernel_references_only_the_base_library()
    {
        var violations = AssembliesReferencedOutsideTheBaseLibrary(Solution.SharedKernel, []);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_reference_only_the_base_library_and_SharedKernel(string module)
    {
        var violations = AssembliesReferencedOutsideTheBaseLibrary($"{module}.Contracts", [Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_references_only_the_base_library_and_SharedKernel(string module)
    {
        var violations = AssembliesReferencedOutsideTheBaseLibrary($"{module}.Domain", [Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_depends_only_on_its_Domain_any_Contracts_and_SharedKernel(string module)
    {
        var violations = TypesDependingOnProjectsOtherThan(
            $"{module}.Application",
            [$"{module}.Domain", .. Solution.LayerOfEveryModule("Contracts"), Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Infrastructure_depends_only_on_its_Application_Domain_Contracts_and_SharedKernel(string module)
    {
        var violations = TypesDependingOnProjectsOtherThan(
            $"{module}.Infrastructure",
            [$"{module}.Application", $"{module}.Domain", $"{module}.Contracts", Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Api_depends_only_on_its_Application_Contracts_Infrastructure_and_SharedKernel(string module)
    {
        var violations = TypesDependingOnProjectsOtherThan(
            $"{module}.Api",
            [$"{module}.Application", $"{module}.Contracts", $"{module}.Infrastructure", Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Only_the_module_registration_class_in_Api_depends_on_Infrastructure(string module)
    {
        var result = Types.InAssembly(Solution.Load($"{module}.Api"))
            .That().MeetCustomRule(type => OutermostType(type).Name != $"{module}Module")
            .ShouldNot().HaveDependencyOnAny($"{module}.Infrastructure")
            .GetResult();

        FailuresOf(result).ShouldBeEmpty();
    }

    [Fact]
    public void Host_depends_only_on_module_Api_projects_and_SharedKernel()
    {
        var violations = TypesDependingOnProjectsOtherThan(
            Solution.Host,
            [.. Solution.LayerOfEveryModule("Api"), Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    private static IEnumerable<string> TypesDependingOnProjectsOtherThan(string project, IEnumerable<string> allowed)
    {
        string[] forbidden = [.. Solution.AllProjects.Except([project, .. allowed])];

        var result = Types.InAssembly(Solution.Load(project))
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        return FailuresOf(result);
    }

    private static IEnumerable<string> FailuresOf(NetArchTest.Rules.TestResult result) =>
        result.FailingTypes?.Select(type => $"{type.FullName}: {type.Explanation}") ?? [];

    // Lambdas and closures compile into nested types, which belong to the class that declares them.
    private static TypeDefinition OutermostType(TypeDefinition type) =>
        type.DeclaringType is null ? type : OutermostType(type.DeclaringType);

    // The base library is the shared framework the runtime itself loads from, so anything outside its directory is either
    // another project of ours or a third-party package.
    private static IEnumerable<string> AssembliesReferencedOutsideTheBaseLibrary(string project, IEnumerable<string> allowed) =>
        Solution.Load(project).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), $"{name}.dll")))
            .Except(allowed);
}
