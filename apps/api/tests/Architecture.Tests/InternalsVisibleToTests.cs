using System.Reflection;
using System.Runtime.CompilerServices;

namespace Architecture.Tests;

public class InternalsVisibleToTests
{
    public static TheoryData<string, string> ModuleProjects =>
        [.. Solution.Modules.SelectMany(module => Solution.ProjectsOf(module).Select(project => (module, project)))];

    [Theory]
    [MemberData(nameof(ModuleProjects))]
    public void ExposeInternals_ModuleProject_OnlyWithinItsModule(string module, string project)
    {
        var friends = FriendAssembliesOf(project);

        friends.ShouldAllBe(friend => friend.StartsWith($"{module}.", StringComparison.Ordinal));
    }

    [Fact]
    public void ExposeInternals_SharedKernel_OnlyToItsOwnTests()
    {
        var friends = FriendAssembliesOf(Solution.SharedKernel);

        friends.ShouldAllBe(friend => friend.StartsWith($"{Solution.SharedKernel}.", StringComparison.Ordinal));
    }

    [Fact]
    public void ExposeInternals_Tenancy_OnlyToItsOwnTests()
    {
        var friends = FriendAssembliesOf(Solution.Tenancy);

        friends.ShouldAllBe(friend => friend.StartsWith($"{Solution.Tenancy}.", StringComparison.Ordinal));
    }

    [Fact]
    public void ExposeInternals_Host_OnlyToItsOwnTests()
    {
        var friends = FriendAssembliesOf(Solution.Host);

        friends.ShouldAllBe(friend => friend.StartsWith($"{Solution.Host}.", StringComparison.Ordinal));
    }

    private static IEnumerable<string> FriendAssembliesOf(string project) =>
        Solution.Load(project)
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim());
}
