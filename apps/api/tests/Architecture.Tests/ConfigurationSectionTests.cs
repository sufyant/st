using System.Reflection;

namespace Architecture.Tests;

// Section 7, configuration rule 1: each module has its own configuration section. A module names each section it binds in a
// Section constant, so the constants of its projects are every section it reads.
public class ConfigurationSectionTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TheoryData<string> Modules => [.. Solution.Modules];

    [Theory]
    [MemberData(nameof(Modules))]
    public void BindConfiguration_InAModule_StartsWithTheModulesName(string module)
    {
        var sections = Solution.ProjectsOf(module)
            .SelectMany(project => Solution.Load(project).GetTypes())
            .SelectMany(type => type.GetFields(Declared))
            .Where(field => field is { IsLiteral: true, Name: "Section" })
            .Select(field => (string)field.GetRawConstantValue()!);

        sections.Where(section => section != module && !section.StartsWith($"{module}:", StringComparison.Ordinal)).ShouldBeEmpty();
    }
}
