using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using Tenancy;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// The tenant onboarding saga through the system door and the real host: its three steps, its compensations, and what is left
// for a person when the last step fails for good. Only Clerk and the email service are fakes.
public sealed class TenantOnboardingProcessTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application), configureServices: services => services.AddFakeLogging());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task OnboardTenant_EveryStepSucceeds_ActivatesTheTenantInvitesTheOwnerOnceAndRecordsIt()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var owner = Email();

        var created = await _api.WaitingForMessagesAsync(() => CreateTenantAsync(admin, Slug(), owner));

        var tenantId = await IdOfAsync(created);
        var invitationId = await InvitationIdAsync(tenantId);
        (await TenantStatusAsync(tenantId)).ShouldBe("Active");
        (await InvitationStatusAsync(tenantId)).ShouldBe("Pending");
        _api.Email.Sent.Where(email => email.To == owner).ShouldHaveSingleItem().IdempotencyKey.ShouldBe($"invite/{invitationId}");
        (await AuditEntriesAsync(tenantId)).ShouldBe($"tenant.created by {await _catalog.UserIdOfAsync(admin)}");
        (await OnboardingStateAsync(tenantId)).ShouldBe("Completed");
    }

    // Section 6, step 1: the tenant, the owner's invitation and the saga record are written in one transaction.
    [Fact]
    public async Task StartOnboarding_TheSagaCannotBeWritten_WritesNoTenantAndNoInvitation()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var slug = Slug();
        var owner = Email();
        await RefuseAsync("tenant_onboardings", "INSERT", $"NEW.tenant_id IN (SELECT id FROM catalog.tenants WHERE slug = '{slug}')");

        var created = await _api.TrackMessagesAsync(() => CreateTenantAsync(admin, slug, owner));

        created.Sent.AllMessages().ShouldBeEmpty();
        (await database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe(0);
        (await database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM catalog.invitations WHERE email = '{owner}'")).ShouldBe(0);
    }

    // Wolverine publishes the fault of a message that went to the dead letter queue with the message's tenant and saga id, so the
    // saga finds its record under row level security.
    [Fact]
    public async Task FailForGood_AStepOfTheOnboarding_TheFaultCarriesItsTenantAndSagaId()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var slug = Slug();
        await RefuseAsync("tenants", "UPDATE", $"NEW.slug = '{slug}' AND NEW.status = 'Active'");

        HttpResponseMessage created = null!;
        var messages = await _api.TrackMessagesAsync(async () => created = await CreateTenantAsync(admin, slug, Email()));

        var tenantId = (await IdOfAsync(created)).ToString();
        var fault = messages.Executed.SingleEnvelope<Fault<ActivateTenant>>();
        fault.TenantId.ShouldBe(tenantId);
        fault.SagaId.ShouldBe(tenantId);
        ((Fault<ActivateTenant>)fault.Message!).TenantId.ShouldBe(tenantId);
    }

    // The activation is refused by the database, as a real failure would be; the step is retried, then compensated.
    [Fact]
    public async Task OnboardTenant_TheActivationKeepsFailing_CancelsTheTenantAndItsInvitation()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var slug = Slug();
        var owner = Email();
        await RefuseAsync("tenants", "UPDATE", $"NEW.slug = '{slug}' AND NEW.status = 'Active'");

        HttpResponseMessage created = null!;
        var messages = await _api.TrackMessagesAsync(async () => created = await CreateTenantAsync(admin, slug, owner));

        var tenantId = await IdOfAsync(created);
        messages.MovedToErrorQueue.SingleMessage<ActivateTenant>().ShouldNotBeNull();
        (await TenantStatusAsync(tenantId)).ShouldBe("Cancelled");
        (await database.ScalarAsSuperuserAsync<string>($"SELECT cancellation_reason FROM catalog.tenants WHERE id = '{tenantId}'"))
            .ShouldBe("activation_failed");
        _api.Services.GetFakeLogCollector().GetSnapshot().ShouldContain(record =>
            record.Id.Name == "TenantCancelled"
            && record.Message.Contains(tenantId.ToString(), StringComparison.Ordinal)
            && record.Message.Contains("activation_failed", StringComparison.Ordinal));
        (await InvitationStatusAsync(tenantId)).ShouldBe("Cancelled");
        (await OnboardingStateAsync(tenantId)).ShouldBe("Cancelled");
        _api.Email.Attempts.ShouldNotContain(attempt => attempt.Email.To == owner);
        (await AuditEntriesAsync(tenantId)).ShouldBeNull();
    }

    // After the pivot nothing is undone: the tenant stays active, the invitation nobody received is cancelled, and a person is told.
    [Fact]
    public async Task OnboardTenant_TheEmailKeepsFailing_LeavesTheTenantActiveCancelsTheInvitationAndRaisesTheAlarm()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        _api.Email.IsDown = true;
        using var alarms = new MetricCollector<long>(_api.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), "Modules.ControlPlane", "tenant_onboarding.needs_attention");

        HttpResponseMessage created = null!;
        var messages = await _api.TrackMessagesAsync(async () => created = await CreateTenantAsync(admin, Slug(), Email()));

        var tenantId = await IdOfAsync(created);
        messages.MovedToErrorQueue.SingleMessage<OwnerInvitationReady>().ShouldNotBeNull();
        (await TenantStatusAsync(tenantId)).ShouldBe("Active");
        (await InvitationStatusAsync(tenantId)).ShouldBe("Cancelled");
        (await OnboardingStateAsync(tenantId)).ShouldBe("NeedsAttention");
        _api.Services.GetFakeLogCollector().GetSnapshot().ShouldContain(record =>
            record.Level == LogLevel.Error
            && record.Id.Name == "TenantOnboardingNeedsAttention"
            && record.Message.Contains(tenantId.ToString(), StringComparison.Ordinal));
        alarms.GetMeasurementSnapshot().ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    // O4: the email is sent under the invitation's idempotency key, so the event that arrives again sends no second email.
    [Fact]
    public async Task SendInvitation_TheEventArrivesTwice_SendsOneEmail()
    {
        var admin = await _catalog.AddSystemAdminAsync();
        var owner = Email();
        var created = await _api.WaitingForMessagesAsync(() => CreateTenantAsync(admin, Slug(), owner));
        var tenantId = await IdOfAsync(created);
        var invitationId = await InvitationIdAsync(tenantId);
        var again = new OwnerInvitationReady(
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, tenantId, invitationId, owner, "Acme Ltd", _api.Email.LinkSentTo(owner));

        await _api.WaitingForMessagesAsync(async () =>
        {
            await Bus().PublishAsync(again, new DeliveryOptions { TenantId = tenantId.ToString(), SagaId = tenantId.ToString() });
            return true;
        });

        _api.Email.Attempts.Where(attempt => attempt.Email.To == owner).Select(attempt => attempt.Email.IdempotencyKey)
            .ShouldBe([$"invite/{invitationId}", $"invite/{invitationId}"]);
        _api.Email.Sent.Where(email => email.To == owner).ShouldHaveSingleItem();
        (await OnboardingStateAsync(tenantId)).ShouldBe("Completed");
    }

    // W6, spike T9: the activation and the email's report reach the saga at the same moment, and both read its record before either
    // writes. The report writes first and completes the onboarding. The activation's write then fails on the version check, and its
    // retry finds the onboarding completed. Without the check, the activation would put the saga back to waiting for an email that
    // was already sent.
    [Fact]
    public async Task UpdateOnboarding_TwoMessagesAtOnce_KeepsBothChanges()
    {
        var tenantId = Guid.CreateVersion7();
        var invitationId = Guid.CreateVersion7();
        await database.ExecuteInTenantAsync(
            tenantId,
            $"""
            INSERT INTO catalog.tenant_onboardings (id, state, invitation_id, invitation_email_timeout, cancellation_timeout, version)
            VALUES ('{tenantId}', 'Activating', '{invitationId}', interval '2 hours', interval '10 minutes', 0)
            """);
        var now = DateTimeOffset.UtcNow;
        var emailSent = new Notifications.Contracts.InvitationEmailSent(Guid.CreateVersion7(), now, tenantId, invitationId);
        var activated = new TenantActivated(Guid.CreateVersion7(), now, tenantId, "Acme Ltd", Guid.CreateVersion7());
        await using var hold = await HoldOnboardingAsync(tenantId);

        var messages = await _api.TrackMessagesAsync(async () =>
        {
            await Bus().PublishAsync(emailSent, new DeliveryOptions { TenantId = tenantId.ToString() });
            await hold.WaitForWritersAsync(1);
            await Bus().PublishAsync(activated, new DeliveryOptions { TenantId = tenantId.ToString() });
            await hold.WaitForWritersAsync(2);
            await hold.ReleaseAsync();
        });

        (await OnboardingStateAsync(tenantId)).ShouldBe("Completed");
        messages.MovedToErrorQueue.Envelopes().ShouldBeEmpty();
        _api.Services.GetFakeLogCollector().GetSnapshot().ShouldContain(record => record.Exception is SagaConcurrencyException);
    }

    private Task<HttpResponseMessage> CreateTenantAsync(string admin, string slug, string ownerEmail) =>
        _api.CreateClient(admin, secondFactor: true)
            .CreateTenantAsync(new { name = "Acme Ltd", slug, ownerEmail });

    private IMessageBus Bus() => _api.Services.GetRequiredService<IHost>().MessageBus();

    private static async Task<Guid> IdOfAsync(HttpResponseMessage created)
    {
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> InvitationIdAsync(Guid tenantId) =>
        await database.ScalarAsSuperuserAsync<Guid>($"SELECT id FROM catalog.invitations WHERE tenant_id = '{tenantId}'");

    private Task<string?> TenantStatusAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<string>($"SELECT status FROM catalog.tenants WHERE id = '{tenantId}'");

    private Task<string?> InvitationStatusAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<string>($"SELECT status FROM catalog.invitations WHERE tenant_id = '{tenantId}'");

    private Task<string?> OnboardingStateAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<string>($"SELECT state FROM catalog.tenant_onboardings WHERE id = '{tenantId}'");

    private Task<string?> AuditEntriesAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<string>(
            $"SELECT string_agg(operation || ' by ' || actor_id, ', ') FROM audit.entries WHERE tenant_id = '{tenantId}'");

    // A database trigger that refuses the statement on matching rows, as a real failure of that statement would.
    private async Task RefuseAsync(string table, string statement, string when)
    {
        var name = $"refuse_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(database.SuperuserConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE FUNCTION catalog.{name}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF {when} THEN RAISE EXCEPTION 'refused'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER {name} BEFORE {statement} ON catalog.{table} FOR EACH ROW EXECUTE FUNCTION catalog.{name}();
            """,
            connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    // Locks the saga's record in a transaction of its own, so the handlers that read it all wait at their write.
    private async Task<OnboardingHold> HoldOnboardingAsync(Guid tenantId)
    {
        var connection = new NpgsqlConnection(database.SuperuserConnectionString);
        await connection.OpenAsync(Cancellation);
        var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using var lockRow = new NpgsqlCommand($"SELECT 1 FROM catalog.tenant_onboardings WHERE id = '{tenantId}' FOR UPDATE", connection, transaction);
        await lockRow.ExecuteNonQueryAsync(Cancellation);

        return new OnboardingHold(database, connection, transaction);
    }

    private static string Slug() => $"tenant-{Guid.NewGuid():N}"[..20];

    private static string Email() => $"{Guid.NewGuid():N}@example.com";

    private sealed class OnboardingHold(Database database, NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        // Waits until the given number of sessions wait to write the saga's record. Only the first waits on this transaction itself;
        // PostgreSQL queues the others behind it, and lets them write in that order.
        public async Task WaitForWritersAsync(int writers)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await using var observer = new NpgsqlConnection(database.SuperuserConnectionString);
            await observer.OpenAsync(timeout.Token);
            await using var waiting = new NpgsqlCommand(
                $"""
                SELECT count(*) FROM pg_stat_activity
                WHERE pid <> {connection.ProcessID} AND wait_event_type = 'Lock' AND query LIKE '%UPDATE catalog.tenant_onboardings%'
                """,
                observer);
            while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < writers)
            {
                timeout.Token.ThrowIfCancellationRequested();
            }
        }

        public Task ReleaseAsync() => transaction.RollbackAsync(Cancellation);

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
