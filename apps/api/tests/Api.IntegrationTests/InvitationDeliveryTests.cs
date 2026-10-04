using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Tenancy;

namespace Api.IntegrationTests;

// Until the Notifications module sends invitation emails through Resend (0029, 0037), only Development writes the link to the
// log; anywhere else an invitation cannot be sent, and the command fails loudly instead of losing the link.
public sealed class InvitationDeliveryTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);

    [Fact]
    public async Task In_development_the_invitation_link_is_written_to_the_log()
    {
        await using var api = Api(Environments.Development);
        var email = $"{Guid.NewGuid():N}@example.com";

        var response = await InviteAsync(api, email);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.Services.GetFakeLogCollector().GetSnapshot().ShouldContain(record => record.Message.Contains(email, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outside_development_an_invitation_fails_with_an_explicit_error_and_is_not_kept()
    {
        await using var api = Api(Environments.Production);
        var email = $"{Guid.NewGuid():N}@example.com";

        var response = await InviteAsync(api, email);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        api.Services.GetFakeLogCollector().GetSnapshot()
            .ShouldContain(record => record.Exception is InvalidOperationException && record.Exception.Message.Contains("Development", StringComparison.Ordinal));
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.invitations WHERE email = '{email}'")).ShouldBe(0);
    }

    private ApiFactory Api(string environment) => new(
        database.ConnectionStringFor(DatabaseRoles.Application),
        environment: environment,
        fakeInvitationSender: false,
        configureServices: services => services.AddFakeLogging());

    private async Task<HttpResponseMessage> InviteAsync(ApiFactory api, string email)
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var roleId = await database.ScalarAsync<Guid>("SELECT id FROM catalog.roles WHERE built_in = 'Member'");

        return await api.CreateClient(owner).PostAsJsonAsync($"/v1/tenants/{tenant.Slug}/invitations", new { email, roleId }, Cancellation);
    }
}
