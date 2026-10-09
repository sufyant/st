using ControlPlane.Application.Ports;
using Notifications.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// The composed application, as Program builds it, in development unless a test names another environment, and against the
// given database, in the role that serves requests and handles messages. Clerk and the email service are systems we do not own, so
// they are fakes unless a test exercises the application's own email channel; the session tokens are signed with the test key. The
// onboarding's retries are short, so a step that fails for good reaches the dead letter queue within a test. A setting a test gives
// as null is left out.
internal sealed class ApiFactory(
    string databaseConnectionString,
    string? migrationsConnectionString = null,
    TimeProvider? time = null,
    string? environment = null,
    IReadOnlyList<string>? authorizedParties = null,
    bool fakeEmailChannel = true,
    Action<IServiceCollection>? configureServices = null,
    string? messagingConnectionString = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string AcceptUrl = "https://app.test/invitations/accept";

    // No account of the fake identity provider has this address, so nobody becomes the first system admin by accident.
    public const string FirstSystemAdminEmail = "first-admin@app.test";

    public FakeIdentityProvider Identity { get; } = new();

    public FakeEmailChannel Email { get; } = new();

    public static readonly IReadOnlyDictionary<string, string?> ShortRetries = new Dictionary<string, string?>
    {
        ["ControlPlane:IdentityProviderRetryDelays:0"] = "00:00:00.010",
        ["ControlPlane:IdentityProviderRetryDelays:1"] = "00:00:00.020",
        ["Notifications:InvitationEmailRetryDelays:0"] = "00:00:00.010",
        ["Notifications:InvitationEmailRetryDelays:1"] = "00:00:00.020",
        ["Notifications:InvitationEmailRetryDelays:2"] = "00:00:00.040",
    };

    public HttpClient CreateClient(string userId, bool secondFactor = false)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, secondFactor));
        return client;
    }

    // Runs the action and waits until every message it caused has been handled, such as the steps of a tenant's onboarding.
    public async Task<T> WaitingForMessagesAsync<T>(Func<Task<T>> action)
    {
        var result = default(T)!;
        await Tracking().ExecuteAndWaitAsync(RunAsync);

        return result;

        async Task RunAsync(IMessageContext _) => result = await action();
    }

    // The same, for a test that expects a message to fail and inspects what became of it.
    public Task<ITrackedSession> TrackMessagesAsync(Func<Task> action)
    {
        return Tracking().DoNotAssertOnExceptionsDetected().ExecuteAndWaitAsync(RunAsync);

        Task RunAsync(IMessageContext _) => action();
    }

    // The same, until the condition holds, for a test that ends while messages still wait, such as a retry scheduled for later.
    public Task<ITrackedSession> TrackMessagesAsync(Func<Task> action, ITrackedCondition until)
    {
        return Tracking().DoNotAssertOnExceptionsDetected().WaitForCondition(until).ExecuteAndWaitAsync(RunAsync);

        Task RunAsync(IMessageContext _) => action();
    }

    private TrackedSessionConfiguration Tracking() => Services.GetRequiredService<IHost>().TrackActivity().Timeout(TimeSpan.FromSeconds(30));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment ?? Environments.Development);

        Dictionary<string, string?> configuration = new()
        {
            ["Host:Role"] = "all",
            ["ConnectionStrings:Database"] = databaseConnectionString,
            ["ConnectionStrings:Messaging"] = messagingConnectionString,
            ["ConnectionStrings:Migrations"] = migrationsConnectionString,
            ["ControlPlane:Invitations:AcceptUrl"] = AcceptUrl,
            ["ControlPlane:Clerk:SecretKey"] = "sk_test_unused",
            ["ControlPlane:FirstSystemAdminEmail"] = FirstSystemAdminEmail,
            ["Authentication:Clerk:Issuer"] = TestTokens.Issuer,
        };
        foreach (var (party, index) in (authorizedParties ?? [TestTokens.AuthorizedParty]).Select((party, index) => (party, index)))
        {
            configuration[$"Authentication:Clerk:AuthorizedParties:{index}"] = party;
        }

        foreach (var (key, value) in ShortRetries.Concat(settings ?? new Dictionary<string, string?>()))
        {
            configuration[key] = value;
        }

        // A host setting given as null would read as an empty value, not as a missing one.
        foreach (var (key, value) in configuration.Where(setting => setting.Value is not null))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.TrustTestKey();
            services.Replace(ServiceDescriptor.Singleton<IIdentityProvider>(Identity));
            if (fakeEmailChannel)
            {
                services.Replace(ServiceDescriptor.Singleton<IEmailChannel>(Email));
            }

            if (time is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(time));
            }

            configureServices?.Invoke(services);
        });
    }
}
