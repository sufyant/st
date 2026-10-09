namespace Architecture.Tests;

// The reference table checked on the project files themselves. The type rules in ReferenceRuleTests see only references
// that some type uses; an unused project reference still lets the next change use it without anyone noticing. Anything the
// table does not list is forbidden.
public class ProjectReferenceTests
{
    public static TheoryData<string, string> ModuleProjects =>
        [.. Solution.Modules.SelectMany(module => Solution.Layers.Select(layer => (module, layer)))];

    [Fact]
    public void SharedKernel_references_no_project()
    {
        var references = Solution.ReadProjectFile(Solution.SharedKernel).ProjectReferences;

        references.ShouldBeEmpty();
    }

    [Fact]
    public void Tenancy_references_only_SharedKernel()
    {
        var references = Solution.ReadProjectFile(Solution.Tenancy).ProjectReferences;

        references.Except([Solution.SharedKernel]).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ModuleProjects))]
    public void ReferenceProjects_ModuleProject_StaysInsideTheTable(string module, string layer)
    {
        var file = Solution.ReadProjectFile($"{module}.{layer}");
        var allowed = ReferenceTable.For(module, layer);

        file.ProjectReferences.Except(allowed.Projects).ShouldBeEmpty();
        file.PackageReferences.Where(package => !allowed.Packages.Any(pattern => Matches(pattern, package))).ShouldBeEmpty();
        file.FrameworkReferences.Except(allowed.Frameworks).ShouldBeEmpty();
    }

    // The host composes every module and may use any package it needs.
    [Fact]
    public void ReferenceProjects_Host_StaysInsideTheTable()
    {
        var references = Solution.ReadProjectFile(Solution.Host).ProjectReferences;

        references.Except([
                .. Solution.LayerOfEveryModule("Api"),
                .. Solution.LayerOfEveryModule("Infrastructure"),
                Solution.SharedKernel,
                Solution.Tenancy,
            ])
            .ShouldBeEmpty();
    }

    // A pattern ending in ".*" names a package family: the package of that name and every package under it.
    private static bool Matches(string pattern, string package) =>
        pattern.EndsWith(".*", StringComparison.Ordinal)
            ? package == pattern[..^2] || package.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : package == pattern;

    private sealed record ReferenceTable(IReadOnlyList<string> Projects, IReadOnlyList<string> Packages, IReadOnlyList<string> Frameworks)
    {
        private const string AspNetCore = "Microsoft.AspNetCore.App";
        private const string Wolverine = "WolverineFx.*";

        public static ReferenceTable For(string module, string layer) => layer switch
        {
            "Contracts" => new([Solution.SharedKernel], [], []),
            "Domain" => new([Solution.SharedKernel], [], []),
            "Application" => new(
                [$"{module}.Domain", .. Solution.LayerOfEveryModule("Contracts"), Solution.SharedKernel],
                [Wolverine, "FluentValidation.*"],
                []),
            "Api" => new([$"{module}.Application", $"{module}.Contracts", Solution.SharedKernel], [Wolverine], [AspNetCore]),
            "Infrastructure" => new(
                [$"{module}.Application", $"{module}.Domain", $"{module}.Contracts", Solution.SharedKernel, Solution.Tenancy],
                ["Microsoft.EntityFrameworkCore.*", "Npgsql.*", "Microsoft.Extensions.*"],
                []),
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Every layer has a row in the table."),
        };
    }
}
