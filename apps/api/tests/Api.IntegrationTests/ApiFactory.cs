using ControlPlane.Application.Ports;
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
// given database. Clerk and the invitation email are systems we do not own, so they are fakes unless a test exercises the
// application's own sender; the session tokens are signed with the test key.
internal sealed class ApiFactory(
    string pooledConnectionString,
    string? migrationsConnectionString = null,
    string? reportingConnectionString = null,
    TimeProvider? time = null,
    string? environment = null,
    IReadOnlyList<string>? authorizedParties = null,
    bool fakeInvitationSender = true,
    Action<IServiceCollection>? configureServices = null,
    string? directConnectionString = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string AcceptUrl = "https://app.test/invitations/accept";

    public FakeIdentityProvider Identity { get; } = new();

    public FakeInvitationSender Sender { get; } = new();

    public HttpClient CreateClient(string userId, bool secondFactor = false)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, secondFactor));
        return client;
    }

    // Runs the action and waits until every message it caused has been handled, such as the delivery of an invitation (0029).
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

    private TrackedSessionConfiguration Tracking() => Services.GetRequiredService<IHost>().TrackActivity().Timeout(TimeSpan.FromSeconds(30));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment ?? Environments.Development);
        builder.UseSetting("ConnectionStrings:Pooled", pooledConnectionString);
        builder.UseSetting("ConnectionStrings:Direct", directConnectionString ?? pooledConnectionString);
        builder.UseSetting("ConnectionStrings:Migrations", migrationsConnectionString);
        builder.UseSetting("ConnectionStrings:Reporting", reportingConnectionString);
        builder.UseSetting("Invitations:AcceptUrl", AcceptUrl);
        builder.UseSetting("Clerk:Issuer", TestTokens.Issuer);
        foreach (var (party, index) in (authorizedParties ?? [TestTokens.AuthorizedParty]).Select((party, index) => (party, index)))
        {
            builder.UseSetting($"Clerk:AuthorizedParties:{index}", party);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.TrustTestKey();
            services.Replace(ServiceDescriptor.Singleton<IIdentityProvider>(Identity));
            if (fakeInvitationSender)
            {
                services.Replace(ServiceDescriptor.Singleton<IInvitationSender>(Sender));
            }

            if (time is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(time));
            }

            configureServices?.Invoke(services);
        });
    }
}
