namespace Api.Hosting;

// The process types one build output runs as (section 1): web serves the API, worker handles messages, all does both, for local
// development and tests.
internal enum HostRole
{
    Web,
    Worker,
    All,
}

// The host's own settings (section Host), checked on start.
internal sealed class HostSettings
{
    public const string Section = "Host";

    public string? Role { get; set; }
}

internal static class HostSettingsExtensions
{
    // The role this host runs as. The setting is read while the host is composed, and checked on start like every other: the
    // migration step composes the host without it and never starts it.
    public static HostRole? AddHostSettings(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<HostSettings>()
            .BindConfiguration(HostSettings.Section)
            .Validate(settings => RoleOf(settings.Role) is not null, $"{HostSettings.Section}:Role must be web, worker or all.")
            .ValidateOnStart();

        return RoleOf(builder.Configuration[$"{HostSettings.Section}:{nameof(HostSettings.Role)}"]);
    }

    private static HostRole? RoleOf(string? role) => role switch
    {
        "web" => HostRole.Web,
        "worker" => HostRole.Worker,
        "all" => HostRole.All,
        _ => null,
    };
}
