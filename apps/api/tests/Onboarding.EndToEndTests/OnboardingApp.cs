using ControlPlane.Application.Ports;
using Notifications.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Onboarding.EndToEndTests;

// The application as Program composes it, running in Production in the given role against the database the deployment steps
// prepared. Clerk and the email channel are fakes at their ports, shared by the hosts of one test. The first system admin is the
// person whose verified email address the configuration names.
internal sealed class OnboardingApp(Database database, string role, FakeIdentityProvider identity, FakeEmailChannel email)
    : WebApplicationFactory<Program>
{
    public const string FirstSystemAdminEmail = "admin@e2e.test";

    public HttpClient ClientFor(string userId, bool secondFactor = false)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, secondFactor));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("Host:Role", role);
        builder.UseSetting("ConnectionStrings:Database", database.ApplicationConnectionString);
        builder.UseSetting("ControlPlane:Invitations:AcceptUrl", "https://app.test/invitations/accept");
        builder.UseSetting("ControlPlane:Clerk:SecretKey", "sk_test_unused");
        builder.UseSetting("ControlPlane:FirstSystemAdminEmail", FirstSystemAdminEmail);
        builder.UseSetting("Authentication:Clerk:Issuer", TestTokens.Issuer);
        builder.UseSetting("Authentication:Clerk:AuthorizedParties:0", TestTokens.AuthorizedParty);
        builder.UseSetting("Notifications:Resend:ApiKey", "re_test_unused");
        builder.UseSetting("Notifications:Resend:From", "no-reply@app.test");

        builder.ConfigureTestServices(services =>
        {
            services.TrustTestKey();
            services.Replace(ServiceDescriptor.Singleton<IIdentityProvider>(identity));
            services.Replace(ServiceDescriptor.Singleton<IEmailChannel>(email));
        });
    }
}
