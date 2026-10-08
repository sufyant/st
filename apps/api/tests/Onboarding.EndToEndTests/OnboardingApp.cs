using ControlPlane.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;

namespace Onboarding.EndToEndTests;

// The application as Program composes it, running in Production against the database the deployment steps prepared. Clerk and the
// email channel are fakes at their ports.
internal sealed class OnboardingApp(Database database) : WebApplicationFactory<Program>
{
    public FakeIdentityProvider Identity { get; } = new();

    public FakeInvitationSender Sender { get; } = new();

    public HttpClient ClientFor(string userId, bool secondFactor = false)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, secondFactor));
        return client;
    }

    // Runs the action and waits until every message it caused has been handled: the steps of the onboarding saga and the
    // invitation's delivery run after the request, through the outbox.
    public async Task<T> WaitingForMessagesAsync<T>(Func<Task<T>> action)
    {
        var result = default(T)!;
        await Services.GetRequiredService<IHost>().TrackActivity().Timeout(TimeSpan.FromSeconds(30)).ExecuteAndWaitAsync(RunAsync);

        return result;

        async Task RunAsync(IMessageContext _) => result = await action();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Pooled", database.ApplicationConnectionString);
        builder.UseSetting("ConnectionStrings:Direct", database.ApplicationConnectionString);
        builder.UseSetting("Invitations:AcceptUrl", "https://app.test/invitations/accept");
        builder.UseSetting("Clerk:Issuer", TestTokens.Issuer);
        builder.UseSetting("Clerk:AuthorizedParties:0", TestTokens.AuthorizedParty);

        builder.ConfigureTestServices(services =>
        {
            services.TrustTestKey();
            services.Replace(ServiceDescriptor.Singleton<IIdentityProvider>(Identity));
            services.Replace(ServiceDescriptor.Singleton<IInvitationSender>(Sender));
        });
    }
}
