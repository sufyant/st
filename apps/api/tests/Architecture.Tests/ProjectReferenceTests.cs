namespace Architecture.Tests;

// The reference table checked on the project files themselves. The type rules in ReferenceRuleTests see only references
// that some type uses; an unused project reference still lets the next change use it without anyone noticing.
public class ProjectReferenceTests
{
    public static TheoryData<string> Modules => [.. Solution.Modules];

    [Fact]
    public void SharedKernel_references_no_project()
    {
        var references = Solution.ProjectReferencesOf(Solution.SharedKernel);

        references.ShouldBeEmpty();
    }

    [Fact]
    public void Tenancy_references_only_SharedKernel()
    {
        var references = Solution.ProjectReferencesOf(Solution.Tenancy);

        references.Except([Solution.SharedKernel]).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_references_only_SharedKernel(string module)
    {
        var references = Solution.ProjectReferencesOf($"{module}.Domain");

        references.Except([Solution.SharedKernel]).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_reference_only_SharedKernel(string module)
    {
        var references = Solution.ProjectReferencesOf($"{module}.Contracts");

        references.Except([Solution.SharedKernel]).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_references_only_its_Domain_any_Contracts_and_SharedKernel(string module)
    {
        var references = Solution.ProjectReferencesOf($"{module}.Application");

        references.Except([$"{module}.Domain", .. Solution.LayerOfEveryModule("Contracts"), Solution.SharedKernel]).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Infrastructure_references_only_its_Application_Domain_Contracts_SharedKernel_and_Tenancy(string module)
    {
        var references = Solution.ProjectReferencesOf($"{module}.Infrastructure");

        references.Except([$"{module}.Application", $"{module}.Domain", $"{module}.Contracts", Solution.SharedKernel, Solution.Tenancy])
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Api_references_only_its_Application_Contracts_Infrastructure_and_SharedKernel(string module)
    {
        var references = Solution.ProjectReferencesOf($"{module}.Api");

        references.Except([$"{module}.Application", $"{module}.Contracts", $"{module}.Infrastructure", Solution.SharedKernel])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Host_references_only_module_Api_projects_SharedKernel_and_Tenancy()
    {
        var references = Solution.ProjectReferencesOf(Solution.Host);

        references.Except([.. Solution.LayerOfEveryModule("Api"), Solution.SharedKernel, Solution.Tenancy]).ShouldBeEmpty();
    }
}
