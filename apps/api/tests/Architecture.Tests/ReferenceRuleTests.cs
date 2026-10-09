using System.Runtime.InteropServices;
using NetArchTest.Rules;

namespace Architecture.Tests;

public class ReferenceRuleTests
{
    private const string DbContext = "Microsoft.EntityFrameworkCore.DbContext";

    public static TheoryData<string> Modules => [.. Solution.Modules];

    public static TheoryData<string> DomainAndContractsProjects =>
        [.. Solution.LayerOfEveryModule("Domain"), .. Solution.LayerOfEveryModule("Contracts")];

    [Fact]
    public void ReferenceAssemblies_SharedKernel_OnlyTheBaseLibrary()
    {
        var violations = AssembliesReferencedOutsideTheBaseLibrary(Solution.SharedKernel, []);

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void DependOnProjects_Tenancy_OnlySharedKernel()
    {
        var violations = TypesDependingOnProjectsOtherThan(Solution.Tenancy, [Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ReferenceAssemblies_Contracts_OnlyTheBaseLibraryAndSharedKernel(string module)
    {
        var violations = AssembliesReferencedOutsideTheBaseLibrary($"{module}.Contracts", [Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ReferenceAssemblies_Domain_OnlyTheBaseLibraryAndSharedKernel(string module)
    {
        var violations = AssembliesReferencedOutsideTheBaseLibrary($"{module}.Domain", [Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void DependOnProjects_Application_OnlyItsDomainAnyContractsAndSharedKernel(string module)
    {
        var violations = TypesDependingOnProjectsOtherThan(
            $"{module}.Application",
            [$"{module}.Domain", .. Solution.LayerOfEveryModule("Contracts"), Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void DependOnProjects_Infrastructure_OnlyItsOwnLayersSharedKernelAndTenancy(string module)
    {
        var violations = TypesDependingOnProjectsOtherThan(
            $"{module}.Infrastructure",
            [$"{module}.Application", $"{module}.Domain", $"{module}.Contracts", Solution.SharedKernel, Solution.Tenancy]);

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void DependOnProjects_Api_OnlyItsApplicationContractsAndSharedKernel(string module)
    {
        var violations = TypesDependingOnProjectsOtherThan(
            $"{module}.Api",
            [$"{module}.Application", $"{module}.Contracts", Solution.SharedKernel]);

        violations.ShouldBeEmpty();
    }

    // Section 2: the Api project cannot reach the DbContext, through its own module's Infrastructure or any other way.
    [Theory]
    [MemberData(nameof(Modules))]
    public void ReachData_FromApi_FindsNoDbContextAndNoInfrastructure(string module)
    {
        var api = Solution.Load($"{module}.Api");

        var typesUsingADbContext = FailuresOf(Types.InAssembly(api).ShouldNot().HaveDependencyOnAny(DbContextTypes()).GetResult());
        var infrastructureReferences = api.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Intersect(Solution.LayerOfEveryModule("Infrastructure"));

        typesUsingADbContext.ShouldBeEmpty();
        infrastructureReferences.ShouldBeEmpty();
    }

    // Requirement 8: the business core and the published contracts do not see the tools.
    [Theory]
    [MemberData(nameof(DomainAndContractsProjects))]
    public void ReferenceTools_FromDomainOrContracts_FindsNoWolverineAndNoEfCore(string project)
    {
        string[] tools = ["Wolverine", "JasperFx", "Microsoft.EntityFrameworkCore"];

        var references = Solution.Load(project).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => tools.Any(tool => name == tool || name.StartsWith($"{tool}.", StringComparison.Ordinal)));

        references.ShouldBeEmpty();
    }

    // The host references every module's Api and Infrastructure. Through them it also sees each module's Contracts, which is
    // how it reads ControlPlane's tenant and system admin directories.
    [Fact]
    public void DependOnProjects_Host_OnlyModuleApiInfrastructureContractsSharedKernelAndTenancy()
    {
        var violations = TypesDependingOnProjectsOtherThan(
            Solution.Host,
            [
                .. Solution.LayerOfEveryModule("Api"),
                .. Solution.LayerOfEveryModule("Infrastructure"),
                .. Solution.LayerOfEveryModule("Contracts"),
                Solution.SharedKernel,
                Solution.Tenancy,
            ]);

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

    // Every DbContext of the solution, and the DbContext type itself.
    private static string[] DbContextTypes() =>
    [
        DbContext,
        .. Solution.AllProjects
            .SelectMany(project => Solution.Load(project).GetTypes())
            .Where(DerivesFromDbContext)
            .Select(type => type.FullName!),
    ];

    private static bool DerivesFromDbContext(Type type) =>
        type.BaseType is { } baseType && (baseType.FullName == DbContext || DerivesFromDbContext(baseType));

    private static IEnumerable<string> FailuresOf(NetArchTest.Rules.TestResult result) =>
        result.FailingTypes?.Select(type => $"{type.FullName}: {type.Explanation}") ?? [];

    // The base library is the shared framework the runtime itself loads from, so anything outside its directory is either
    // another project of ours or a third-party package.
    private static IEnumerable<string> AssembliesReferencedOutsideTheBaseLibrary(string project, IEnumerable<string> allowed) =>
        Solution.Load(project).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), $"{name}.dll")))
            .Except(allowed);
}
