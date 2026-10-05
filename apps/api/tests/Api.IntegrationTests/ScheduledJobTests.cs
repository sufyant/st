using System.Net;
using ControlPlane.Api;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenancy;

namespace Api.IntegrationTests;

// Hangfire runs the system-defined recurring jobs once across all pods (0027); its dashboard is open only in local development.
public sealed class ScheduledJobTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);

    [Fact]
    public async Task Starting_the_application_schedules_the_system_jobs()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);
        api.CreateClient();

        using var storage = api.Services.GetRequiredService<JobStorage>().GetConnection();

        var jobs = storage.GetRecurringJobs().Select(job => (job.Id, job.Cron)).ToList();
        jobs.ShouldContain(("controlplane.close-expired-invitations", Cron.Hourly()));
        jobs.ShouldContain(("notifications.scan-due-notifications", Cron.Minutely()));
    }

    // The job sends a command through the host's pipeline; the command closes the invitations whose time has run out.
    [Fact]
    public async Task The_invitation_job_closes_expired_invitations()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);
        var tenant = await _catalog.AddTenantAsync();
        var expired = await AddInvitationAsync(tenant.Id, expiresAt: "now() - interval '1 minute'");
        var current = await AddInvitationAsync(tenant.Id, expiresAt: "now() + interval '1 day'");

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<CloseExpiredInvitationsJob>(scope.ServiceProvider).RunAsync(Cancellation);
        }

        (await StatusOfAsync(expired)).ShouldBe("Expired");
        (await StatusOfAsync(current)).ShouldBe("Pending");
    }

    [Fact]
    public async Task Outside_development_the_dashboard_is_closed()
    {
        await using var api = new ApiFactory(
            database.ApplicationConnectionString,
            environment: Environments.Production,
            settings: new Dictionary<string, string?> { ["Resend:ApiKey"] = "re_test_key", ["Resend:From"] = "no-reply@app.test" });

        var response = await api.CreateClient().GetAsync("/hangfire", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // In development the dashboard is there, for local requests only; the test server's requests carry no address.
    [Fact]
    public async Task In_development_the_dashboard_is_there()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var response = await api.CreateClient().GetAsync("/hangfire", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> AddInvitationAsync(Guid tenantId, string expiresAt)
    {
        var id = Guid.NewGuid();
        var inviter = await database.ScalarAsync<Guid>("INSERT INTO catalog.users (id, external_id) VALUES (gen_random_uuid(), 'user_' || gen_random_uuid()) RETURNING id");
        await database.ScalarAsync<object>(
            $"""
            INSERT INTO catalog.invitations (id, tenant_id, email, role_id, invited_by, created_at, expires_at, status)
            SELECT '{id}', '{tenantId}', '{id:N}@example.com', id, '{inviter}', now() - interval '8 days', {expiresAt}, 'Pending'
            FROM catalog.roles WHERE built_in = 'Member'
            """);

        return id;
    }

    private Task<string?> StatusOfAsync(Guid invitationId) =>
        database.ScalarAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'");
}
