using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tenancy;

namespace Api.IntegrationTests;

// Section 7, configuration rule 2: every setting is checked while the application starts. A required setting that is missing, or
// a value that is wrong, stops the start, and the error names the setting's full key. Each case runs on a database of its own whose
// staff list is empty, so the first system admin's setting is required there too.
public sealed class HostConfigurationTests(Database database)
{
    // The first key of each row is the one the error names; the others are missing too.
    public static TheoryData<string, string[]> RequiredSettings => new()
    {
        { Environments.Development, ["Host:Role"] },
        { Environments.Development, ["ConnectionStrings:Database"] },
        { Environments.Development, ["Authentication:Clerk:Issuer"] },
        { Environments.Development, ["ControlPlane:Clerk:SecretKey"] },
        { Environments.Development, ["ControlPlane:Invitations:AcceptUrl"] },
        { Environments.Development, ["ControlPlane:FirstSystemAdminEmail"] },
        { Environments.Production, ["Authentication:Clerk:AuthorizedParties:0"] },
        { Environments.Production, ["Notifications:Resend:ApiKey"] },
        { Environments.Production, ["Notifications:Resend:From"] },

        // S1: without a connection, the first system admin's check cannot read the staff list; the start names the connection.
        { Environments.Development, ["ConnectionStrings:Database", "ControlPlane:FirstSystemAdminEmail"] },
    };

    [Theory]
    [MemberData(nameof(RequiredSettings))]
    public async Task StartApplication_WithoutARequiredSetting_FailsNamingTheKey(string environment, string[] missing)
    {
        await using var api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application, await database.CreateMigratedDatabaseAsync()),
            environment: environment,
            settings: Without(missing));

        var start = () => api.CreateClient();

        start.ShouldThrow<Exception>().Message.ShouldContain(missing[0].TrimEnd(':', '0'));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("web,worker")]
    [InlineData("1")]
    public async Task StartApplication_WithAnUnknownRole_FailsNamingTheKey(string role)
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, settings: new Dictionary<string, string?> { ["Host:Role"] = role });

        var start = () => api.CreateClient();

        start.ShouldThrow<Exception>().Message.ShouldContain("Host:Role");
    }

    public static TheoryData<string, string> WrongSettings => new()
    {
        { "ControlPlane:FirstSystemAdminEmail", "not an email" },
        { "ControlPlane:Invitations:AcceptUrl", "/invitations/accept" },
        { "ControlPlane:Clerk:Timeout", "00:00:00" },
        { "ControlPlane:ActivationTimeout", "00:00:00" },
        { "ControlPlane:InvitationEmailTimeout", "-00:00:01" },
        { "ControlPlane:CancellationTimeout", "00:00:00" },
        { "RateLimiting:InvitationAccept:PermitLimit", "0" },
        { "RateLimiting:InvitationAccept:Window", "00:00:00" },
        { "Notifications:Resend:Timeout", "-00:00:01" },
        { "Host:ShutdownTimeout", "00:00:00" },
        { "Host:Cors:AllowedOrigins:0", "*" },
        { "Host:Cors:AllowedOrigins:0", "https://app.test/path" },
    };

    [Theory]
    [MemberData(nameof(WrongSettings))]
    public async Task StartApplication_WithAWrongSetting_FailsNamingTheKey(string key, string value)
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, settings: new Dictionary<string, string?> { [key] = value });

        var start = () => api.CreateClient();

        start.ShouldThrow<Exception>().Message.ShouldContain(key.TrimEnd(':', '0'));
    }

    // Section 6: once the staff list has a system admin, the first system admin's setting has no effect.
    [Fact]
    public async Task StartApplication_WithoutTheFirstSystemAdminEmailOnceTheStaffListHasAnAdmin_Starts()
    {
        var name = await database.CreateMigratedDatabaseAsync();
        await database.ScalarAsync<object>(
            $"""
            INSERT INTO catalog.users (id, external_id) VALUES ('{Guid.NewGuid()}', 'user_staff');
            INSERT INTO catalog.system_admins (user_id, granted_at) SELECT id, now() FROM catalog.users WHERE external_id = 'user_staff';
            """,
            database: name);
        await using var api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application, name),
            settings: new Dictionary<string, string?> { ["ControlPlane:FirstSystemAdminEmail"] = null });

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Twelve-Factor IX: the host waits this long for its work to finish when it is asked to stop.
    [Theory]
    [InlineData(null, 30)]
    [InlineData("00:00:45", 45)]
    public async Task StopApplication_ShutdownTimeout_IsTheConfiguredTimeOrThirtySeconds(string? setting, int seconds)
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString, settings: new Dictionary<string, string?> { ["Host:ShutdownTimeout"] = setting });

        var options = api.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        options.ShutdownTimeout.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    // Everything a production start needs, so that leaving out one setting is the only thing wrong.
    // The complete settings without the given keys; a setting given as null is left out.
    private static Dictionary<string, string?> Without(string[] keys) =>
        Complete.Where(setting => !keys.Contains(setting.Key)).Concat(keys.Select(key => KeyValuePair.Create(key, (string?)null))).ToDictionary();

    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["Notifications:Resend:ApiKey"] = "re_test_key",
        ["Notifications:Resend:From"] = "no-reply@app.test",
    };
}
