using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Architecture.Tests;

// Smoke findings 8 and 11: docs/configuration.md has a row for every key the application binds. The keys are read from the composed
// application: every options class of the solution that is bound to a configuration section, and the settable properties under it.
public partial class ConfigurationDocumentTests
{
    [Fact]
    public async Task DocumentConfiguration_EveryKeyBoundInCode_HasARow()
    {
        var bound = await KeysBoundInCodeAsync();

        var undocumented = bound.Except(DocumentedKeys());

        bound.ShouldContain("Host:Role");
        undocumented.ShouldBeEmpty();
    }

    // The first cell of each table row, when it is a key in backticks.
    private static HashSet<string> DocumentedKeys() =>
    [
        .. File.ReadLines(Path.Combine(Solution.Root, "docs", "configuration.md"))
            .Select(line => TableKey().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value),
    ];

    private static async Task<HashSet<string>> KeysBoundInCodeAsync()
    {
        List<Type> optionsClasses = [];
        await using var application = new ComposedApplication(configure: builder =>
            builder.ConfigureTestServices(services => optionsClasses.AddRange(OptionsClassesBoundIn(services))));
        var services = application.Services;

        return
        [
            .. optionsClasses.SelectMany(options => SectionsOf(options, services)
                .SelectMany(section => SettingsOf(options).Select(setting => $"{section}:{setting}"))),
        ];
    }

    // Every options class of the solution that is bound to configuration: binding registers a change token source for it.
    private static List<Type> OptionsClassesBoundIn(IServiceCollection services) =>
    [
        .. services
            .Select(service => service.ServiceType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptionsChangeTokenSource<>))
            .Select(type => type.GetGenericArguments()[0])
            .Where(options => Solution.AllProjects.Contains(options.Assembly.GetName().Name!))
            .Distinct(),
    ];

    // BindConfiguration registers a step that configures the options from the configuration; the section it was given is the
    // configSectionPath the step's delegate captured.
    private static IEnumerable<string> SectionsOf(Type options, IServiceProvider services) =>
        services.GetServices(typeof(IConfigureOptions<>).MakeGenericType(options))
            .Where(step => step!.GetType().IsGenericType && step.GetType().GetGenericTypeDefinition() == typeof(ConfigureNamedOptions<,>))
            .Where(step => step!.GetType().GetGenericArguments()[1] == typeof(IConfiguration))
            .Select(step => ((Delegate)step!.GetType().GetProperty("Action")!.GetValue(step)!).Target)
            .Select(closure => closure?.GetType().GetField("configSectionPath")?.GetValue(closure) as string)
            .OfType<string>();

    // The paths of the settable values and lists of an options class, and of the classes it holds.
    private static IEnumerable<string> SettingsOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod?.IsPublic == true)
            .SelectMany(property => IsValue(property.PropertyType)
                ? property.SetMethod?.IsPublic == true ? [property.Name] : []
                : SettingsOf(property.PropertyType).Select(setting => $"{property.Name}:{setting}"));

    private static bool IsValue(Type type) =>
        type.IsValueType || type == typeof(string) || type == typeof(Uri) || typeof(IEnumerable).IsAssignableFrom(type);

    [GeneratedRegex(@"^\| `(?<key>[^`]+)` \|")]
    private static partial Regex TableKey();
}
