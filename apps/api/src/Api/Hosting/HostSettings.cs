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

    // How long a stopping host may take to finish the work it has (Twelve-Factor IX).
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // The role, or null when the setting is missing or names no role; the start then fails on it.
    public HostRole? RoleOrNull => Role switch
    {
        "web" => HostRole.Web,
        "worker" => HostRole.Worker,
        "all" => HostRole.All,
        _ => null,
    };
}

internal static class HostSettingsExtensions
{
    // The settings are read while the host is composed, since the role decides what it is composed of, and checked on start like
    // every other setting: the migration step composes the host without them and never starts it.
    public static HostSettings AddHostSettings(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<HostSettings>()
            .BindConfiguration(HostSettings.Section)
            .Validate(settings => settings.RoleOrNull is not null, $"{HostSettings.Section}:Role must be web, worker or all.")
            .Validate(settings => settings.ShutdownTimeout > TimeSpan.Zero, $"{HostSettings.Section}:ShutdownTimeout must be positive.")
            .ValidateOnStart();
        var settings = builder.Configuration.GetSection(HostSettings.Section).Get<HostSettings>() ?? new HostSettings();
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = settings.ShutdownTimeout);

        return settings;
    }

    public static bool ServesTheApi(this HostRole? role) => role is not HostRole.Worker;
}
